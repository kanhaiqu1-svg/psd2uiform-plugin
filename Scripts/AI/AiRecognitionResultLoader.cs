/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 *
 * The software and associated documentation (including but not limited to source code,
 * object code, documents, images, audio, and video files) are the intellectual property
 * of Beijing Yiqu Technology Co., Ltd.
 *
 * For commercial use or licensing inquiries, please contact:
 * Email: efunstudio@gmail.com
 * Website: https://efunstudio.cn
 */

#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiRecognitionResultLoader
    {
        internal bool TryLoadMainType(AiJobContext context, AiAnalysisPackageDocument package, out AiMainTypeResultDocument document, out string error)
        {
            document = null;
            error = null;
            if (context == null || package == null || package.nodes == null)
            {
                error = "MainType load context is invalid.";
                return false;
            }

            if (!AiJobFileUtility.TryReadJson(context.MainTypePath, out AiMainTypeResultDocument source) || source == null)
            {
                error = $"MainType result not found: {context.MainTypePath}";
                return false;
            }

            return TryNormalizeMainType(package, source.treeHash, source.nodes, out document, out error);
        }

        internal bool TryLoadChildRelations(AiJobContext context, AiAnalysisPackageDocument package, out AiChildRelationResultDocument document, out string error)
        {
            document = null;
            error = null;
            if (context == null || package == null || package.nodes == null)
            {
                error = "ChildRelation load context is invalid.";
                return false;
            }

            if (!AiJobFileUtility.TryReadJson(context.ChildRelationPath, out AiChildRelationResultDocument source) || source == null)
            {
                error = $"ChildRelation result not found: {context.ChildRelationPath}";
                return false;
            }

            return TryNormalizeChildRelations(package, source.treeHash, source.relations, out document, out error);
        }

        internal bool TryLoadCombinedRecognition(AiJobContext context, AiAnalysisPackageDocument package, out AiRecognitionCombinedResultDocument document, out string error)
        {
            document = null;
            error = null;
            if (context == null || package == null || package.nodes == null)
            {
                error = "RecognitionCombined load context is invalid.";
                return false;
            }

            if (!AiJobFileUtility.TryReadJson(context.RecognitionCombinedPath, out AiRecognitionCombinedResultDocument source) || source == null)
            {
                error = $"RecognitionCombined result not found: {context.RecognitionCombinedPath}";
                return false;
            }

            return TryNormalizeCombinedRecognition(package, source, out document, out error);
        }

        internal bool TrySplitCombinedRecognition(AiAnalysisPackageDocument package, AiRecognitionCombinedResultDocument combined, out AiMainTypeResultDocument mainTypes, out AiChildRelationResultDocument childRelations, out string error)
        {
            mainTypes = null;
            childRelations = null;
            error = null;
            if (package == null || package.nodes == null || combined == null)
            {
                error = "RecognitionCombined split context is invalid.";
                return false;
            }

            if (!TryNormalizeCombinedRecognition(package, combined, out var normalized, out error))
            {
                return false;
            }

            var currentTypes = BuildCurrentTypeMap(package.nodes);
            var nodeEntries = new List<AiMainTypeEntry>(package.nodes.Count);
            var labelByNodeId = new Dictionary<string, AiRecognitionNodeLabelEntry>(StringComparer.OrdinalIgnoreCase);
            if (normalized.nodeLabels != null)
            {
                for (int i = 0; i < normalized.nodeLabels.Count; i++)
                {
                    var label = normalized.nodeLabels[i];
                    if (label != null && !string.IsNullOrWhiteSpace(label.nodeId))
                    {
                        labelByNodeId[label.nodeId] = label;
                    }
                }
            }

            var ownerCarrierTypes = BuildOwnerCarrierTypeMap(normalized);
            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id) || !labelByNodeId.TryGetValue(node.id, out var label) || label == null)
                {
                    continue;
                }

                string predicted = label.labelType;
                if (ownerCarrierTypes.TryGetValue(node.id, out var ownerType))
                {
                    predicted = ownerType.ToString();
                }

                nodeEntries.Add(new AiMainTypeEntry
                {
                    targetId = node.id,
                    currentUIType = currentTypes.TryGetValue(node.id, out var currentType) ? currentType.ToString() : GUIType.Null.ToString(),
                    predictedUIType = predicted,
                    confidence = NormalizeConfidence(label.confidence),
                    reason = NormalizeReason(label.reason, "AI 节点保底标签。")
                });
            }

            var relationEntries = new List<AiChildRelationEntry>(normalized.roles != null ? normalized.roles.Count : 0);
            var ownerCarrierIds = BuildOwnerCarrierNodeIdMap(normalized);
            if (normalized.roles != null)
            {
                for (int i = 0; i < normalized.roles.Count; i++)
                {
                    var role = normalized.roles[i];
                    if (role == null)
                    {
                        continue;
                    }

                    relationEntries.Add(new AiChildRelationEntry
                    {
                        ownerId = ownerCarrierIds.TryGetValue(role.ownerId, out var carrierNodeId) ? carrierNodeId : role.ownerId ?? string.Empty,
                        targetId = role.carrierNodeId ?? string.Empty,
                        roleType = role.roleType ?? string.Empty,
                        memberNodeIds = CloneArray(role.memberNodeIds),
                        confidence = NormalizeConfidence(role.confidence),
                        reason = NormalizeReason(role.reason, "AI 子控件归属识别结果。")
                    });
                }
            }

            mainTypes = new AiMainTypeResultDocument
            {
                version = AiProtocolVersions.MainType,
                treeHash = package.treeHash ?? string.Empty,
                nodes = nodeEntries
            };
            childRelations = new AiChildRelationResultDocument
            {
                version = AiProtocolVersions.ChildRelation,
                treeHash = package.treeHash ?? string.Empty,
                relations = relationEntries
            };
            return true;
        }

        internal bool TryLoadStructuralPlan(AiJobContext context, AiAnalysisPackageDocument package, out AiStructuralResultDocument document, out string error)
        {
            error = null;
            document = new AiStructuralResultDocument
            {
                version = AiProtocolVersions.Structural,
                treeHash = package != null ? package.treeHash ?? string.Empty : string.Empty,
                operations = new List<AiPatchOperation>()
            };
            return true;
        }

        private static bool TryNormalizeCombinedRecognition(
            AiAnalysisPackageDocument package,
            AiRecognitionCombinedResultDocument source,
            out AiRecognitionCombinedResultDocument document,
            out string error)
        {
            document = null;
            error = null;
            if (package == null || package.nodes == null || source == null)
            {
                error = "RecognitionCombined normalize context is invalid.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(package.treeHash)
                && !string.IsNullOrWhiteSpace(source.treeHash)
                && !string.Equals(package.treeHash, source.treeHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "RecognitionCombined result treeHash mismatch.";
                return false;
            }

            var nodeById = BuildNodeMap(package.nodes);
            if (!TryNormalizeOwners(nodeById, source.owners, out var owners, out error))
            {
                return false;
            }

            if (!TryNormalizeRoles(nodeById, owners, source.roles, out var roles, out error))
            {
                return false;
            }

            if (!TryNormalizeNodeLabels(package.nodes, source.nodeLabels, strictCoverage: true, out var nodeLabels, out error))
            {
                return false;
            }

            if (owners.Count < 1)
            {
                error = "RecognitionCombined result does not contain any valid owners.";
                return false;
            }

            if (source.roles != null && source.roles.Count > 0 && roles.Count < 1)
            {
                error = "RecognitionCombined roles are incompatible with the current protocol.";
                return false;
            }

            document = new AiRecognitionCombinedResultDocument
            {
                version = AiProtocolVersions.RecognitionCombined,
                treeHash = package.treeHash ?? string.Empty,
                owners = owners,
                roles = roles,
                nodeLabels = nodeLabels
            };
            return true;
        }

        private static bool TryNormalizeOwners(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<AiRecognitionOwnerEntry> sourceOwners,
            out List<AiRecognitionOwnerEntry> owners,
            out string error)
        {
            owners = new List<AiRecognitionOwnerEntry>(sourceOwners != null ? sourceOwners.Count : 0);
            error = null;
            if (sourceOwners == null)
            {
                return true;
            }

            var bestByOwnerId = new Dictionary<string, AiRecognitionOwnerEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceOwners.Count; i++)
            {
                var entry = sourceOwners[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ownerId))
                {
                    continue;
                }

                string carrierNodeId = NormalizeExistingNodeId(entry.carrierNodeId, nodeById);
                var memberNodeIds = NormalizeMemberNodeIds(entry.memberNodeIds, carrierNodeId, nodeById);
                if (memberNodeIds.Length < 1)
                {
                    continue;
                }

                if (!TryResolveOwnerType(entry.ownerType, carrierNodeId, memberNodeIds, nodeById, out var ownerType))
                {
                    continue;
                }

                ownerType = AiHelperContractCatalog.NormalizeAiType(ownerType);
                if (ownerType == GUIType.Text)
                {
                    if (!TryResolveSingleTextMember(memberNodeIds, carrierNodeId, nodeById, out var textNodeId))
                    {
                        continue;
                    }

                    carrierNodeId = textNodeId;
                    memberNodeIds = new[] { textNodeId };
                }

                var canonical = new AiRecognitionOwnerEntry
                {
                    ownerId = entry.ownerId.Trim(),
                    ownerType = ownerType.ToString(),
                    carrierNodeId = carrierNodeId,
                    memberNodeIds = memberNodeIds,
                    confidence = CoerceConfidence(entry.confidence),
                    reason = NormalizeReason(entry.reason, "AI owner 识别结果。")
                };

                if (!bestByOwnerId.TryGetValue(canonical.ownerId, out var existing)
                    || GetOwnerScore(canonical) > GetOwnerScore(existing))
                {
                    bestByOwnerId[canonical.ownerId] = canonical;
                }
            }

            owners.AddRange(bestByOwnerId.Values);
            owners.Sort((left, right) => GetOwnerScore(right).CompareTo(GetOwnerScore(left)));
            return true;
        }

        private static bool TryNormalizeRoles(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<AiRecognitionOwnerEntry> owners,
            List<AiRecognitionRoleEntry> sourceRoles,
            out List<AiRecognitionRoleEntry> roles,
            out string error)
        {
            roles = new List<AiRecognitionRoleEntry>(sourceRoles != null ? sourceRoles.Count : 0);
            error = null;
            if (sourceRoles == null)
            {
                return true;
            }

            var ownerById = new Dictionary<string, AiRecognitionOwnerEntry>(StringComparer.OrdinalIgnoreCase);
            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner != null && !string.IsNullOrWhiteSpace(owner.ownerId))
                    {
                        ownerById[owner.ownerId] = owner;
                    }
                }
            }

            var bestByOwnerRole = new Dictionary<string, AiRecognitionRoleEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceRoles.Count; i++)
            {
                var entry = sourceRoles[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ownerId))
                {
                    continue;
                }

                if (!ownerById.TryGetValue(entry.ownerId.Trim(), out var owner) || owner == null)
                {
                    continue;
                }

                string carrierNodeId = NormalizeExistingNodeId(entry.carrierNodeId, nodeById);
                var memberNodeIds = NormalizeMemberNodeIds(entry.memberNodeIds, carrierNodeId, nodeById);
                if (memberNodeIds.Length < 1)
                {
                    continue;
                }

                GUIType ownerType = ParseOwnerType(owner.ownerType);
                if (!TryResolveRoleType(entry.roleType, ownerType, carrierNodeId, memberNodeIds, nodeById, out var roleType))
                {
                    continue;
                }

                if (!AiHelperContractCatalog.AllowsRole(ownerType, roleType))
                {
                    continue;
                }

                if (AiHelperContractCatalog.IsTextRole(roleType))
                {
                    if (!TryResolveSingleTextMember(memberNodeIds, carrierNodeId, nodeById, out var textNodeId))
                    {
                        continue;
                    }

                    carrierNodeId = textNodeId;
                    memberNodeIds = new[] { textNodeId };
                }
                else
                {
                    for (int memberIndex = 0; memberIndex < memberNodeIds.Length; memberIndex++)
                    {
                        if (nodeById.TryGetValue(memberNodeIds[memberIndex], out var memberNode) && memberNode != null && memberNode.isTextLayer)
                        {
                            carrierNodeId = null;
                            memberNodeIds = Array.Empty<string>();
                            break;
                        }
                    }

                    if (memberNodeIds.Length < 1)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(carrierNodeId)
                        && nodeById.TryGetValue(carrierNodeId, out var carrierNode)
                        && carrierNode != null
                        && carrierNode.isTextLayer)
                    {
                        continue;
                    }

                    if (memberNodeIds.Length > 1 && !AiHelperContractCatalog.AllowsMultiMemberRole(roleType))
                    {
                        continue;
                    }
                }

                string ownerRoleKey = owner.ownerId + "\n" + roleType;
                var canonical = new AiRecognitionRoleEntry
                {
                    ownerId = owner.ownerId,
                    roleType = roleType.ToString(),
                    carrierNodeId = carrierNodeId,
                    memberNodeIds = memberNodeIds,
                    confidence = CoerceConfidence(entry.confidence),
                    reason = NormalizeReason(entry.reason, "AI role 识别结果。")
                };

                if (!bestByOwnerRole.TryGetValue(ownerRoleKey, out var existing)
                    || GetRoleScore(canonical) > GetRoleScore(existing))
                {
                    bestByOwnerRole[ownerRoleKey] = canonical;
                }
            }

            var ordered = new List<AiRecognitionRoleEntry>(bestByOwnerRole.Values);
            ordered.Sort((left, right) => GetRoleScore(right).CompareTo(GetRoleScore(left)));

            var claimedMemberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                var role = ordered[i];
                if (role == null)
                {
                    continue;
                }

                bool conflicts = false;
                for (int j = 0; j < role.memberNodeIds.Length; j++)
                {
                    if (!claimedMemberIds.Add(role.memberNodeIds[j]))
                    {
                        conflicts = true;
                        break;
                    }
                }

                if (conflicts)
                {
                    for (int j = 0; j < role.memberNodeIds.Length; j++)
                    {
                        claimedMemberIds.Remove(role.memberNodeIds[j]);
                    }
                    continue;
                }

                roles.Add(role);
            }

            return true;
        }

        private static bool TryNormalizeNodeLabels(
            List<AiAnalysisNodeEntry> packageNodes,
            List<AiRecognitionNodeLabelEntry> sourceLabels,
            bool strictCoverage,
            out List<AiRecognitionNodeLabelEntry> labels,
            out string error)
        {
            labels = new List<AiRecognitionNodeLabelEntry>(packageNodes != null ? packageNodes.Count : 0);
            error = null;
            if (packageNodes == null)
            {
                error = "RecognitionCombined nodeLabel normalization requires package nodes.";
                return false;
            }

            var nodeById = BuildNodeMap(packageNodes);
            var bestByNodeId = new Dictionary<string, AiRecognitionNodeLabelEntry>(StringComparer.OrdinalIgnoreCase);
            if (sourceLabels != null)
            {
                for (int i = 0; i < sourceLabels.Count; i++)
                {
                    var entry = sourceLabels[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                    {
                        continue;
                    }

                    if (!nodeById.TryGetValue(entry.nodeId.Trim(), out var node) || node == null)
                    {
                        continue;
                    }

                    var canonical = new AiRecognitionNodeLabelEntry
                    {
                        nodeId = entry.nodeId.Trim(),
                        currentUIType = ResolveFallbackCurrentType(node).ToString(),
                        labelType = ResolveNodeLabelType(entry.labelType, node).ToString(),
                        confidence = CoerceConfidence(entry.confidence),
                        reason = NormalizeReason(entry.reason, "AI 节点保底标签。")
                    };

                    if (!bestByNodeId.TryGetValue(canonical.nodeId, out var existing) || canonical.confidence > existing.confidence)
                    {
                        bestByNodeId[canonical.nodeId] = canonical;
                    }
                }
            }

            for (int i = 0; i < packageNodes.Count; i++)
            {
                var node = packageNodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                if (!bestByNodeId.TryGetValue(node.id, out var label) || label == null)
                {
                    label = new AiRecognitionNodeLabelEntry
                    {
                        nodeId = node.id,
                        currentUIType = ResolveFallbackCurrentType(node).ToString(),
                        labelType = AiHelperContractCatalog.ResolveSafeNodeLabelFallback(node).ToString(),
                        confidence = strictCoverage ? 0.8f : 0.7f,
                        reason = "本地补齐 nodeLabel。"
                    };
                }

                labels.Add(label);
            }

            return true;
        }

        private static bool TryNormalizeMainType(AiAnalysisPackageDocument package, string sourceTreeHash, List<AiMainTypeEntry> sourceNodes, out AiMainTypeResultDocument document, out string error)
        {
            document = null;
            error = null;
            if (package == null || package.nodes == null)
            {
                error = "MainType normalize context is invalid.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(package.treeHash)
                && !string.IsNullOrWhiteSpace(sourceTreeHash)
                && !string.Equals(package.treeHash, sourceTreeHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "MainType result treeHash mismatch.";
                return false;
            }

            var bestById = new Dictionary<string, AiMainTypeEntry>(StringComparer.OrdinalIgnoreCase);
            if (sourceNodes != null)
            {
                for (int i = 0; i < sourceNodes.Count; i++)
                {
                    var entry = sourceNodes[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.targetId))
                    {
                        continue;
                    }

                    if (!AiHelperContractCatalog.TryParseOwnerType(entry.predictedUIType, out var predictedType))
                    {
                        continue;
                    }

                    if (entry.confidence <= 0f || entry.confidence > 1f)
                    {
                        error = $"MainType entry '{entry.targetId}' has invalid confidence.";
                        return false;
                    }

                    entry.currentUIType = NormalizeCurrentUiTypeString(entry.currentUIType);
                    entry.predictedUIType = predictedType.ToString();
                    entry.reason = NormalizeReason(entry.reason, "AI 主类型识别结果。");

                    if (!bestById.TryGetValue(entry.targetId, out var existing) || entry.confidence > existing.confidence)
                    {
                        bestById[entry.targetId] = entry;
                    }
                }
            }

            var ordered = new List<AiMainTypeEntry>(package.nodes.Count);
            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                if (!bestById.TryGetValue(node.id, out var entry))
                {
                    error = $"MainType result is missing node '{node.id}'.";
                    return false;
                }

                ordered.Add(entry);
            }

            document = new AiMainTypeResultDocument
            {
                version = AiProtocolVersions.MainType,
                treeHash = package.treeHash ?? string.Empty,
                nodes = ordered
            };
            return true;
        }

        private static bool TryNormalizeChildRelations(AiAnalysisPackageDocument package, string sourceTreeHash, List<AiChildRelationEntry> sourceRelations, out AiChildRelationResultDocument document, out string error)
        {
            document = null;
            error = null;
            if (package == null || package.nodes == null)
            {
                error = "ChildRelation normalize context is invalid.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(package.treeHash)
                && !string.IsNullOrWhiteSpace(sourceTreeHash)
                && !string.Equals(package.treeHash, sourceTreeHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "ChildRelation result treeHash mismatch.";
                return false;
            }

            var nodeById = BuildNodeMap(package.nodes);
            var bestByOwnerRole = new Dictionary<string, AiChildRelationEntry>(StringComparer.OrdinalIgnoreCase);
            if (sourceRelations != null)
            {
                for (int i = 0; i < sourceRelations.Count; i++)
                {
                    var entry = sourceRelations[i];
                    if (entry != null && (entry.confidence <= 0f || entry.confidence > 1f))
                    {
                        error = $"ChildRelation entry[{i}] has invalid confidence.";
                        return false;
                    }

                    if (!TryCanonicalizeRelation(entry, nodeById, out var canonical))
                    {
                        continue;
                    }

                    string key = canonical.ownerId + "\n" + canonical.roleType;
                    if (!bestByOwnerRole.TryGetValue(key, out var existing) || GetLegacyRelationScore(canonical) > GetLegacyRelationScore(existing))
                    {
                        bestByOwnerRole[key] = canonical;
                    }
                }
            }

            var selected = new List<AiChildRelationEntry>(bestByOwnerRole.Values);
            selected.Sort((left, right) => GetLegacyRelationScore(right).CompareTo(GetLegacyRelationScore(left)));

            var claimedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                var relation = selected[i];
                if (relation == null || relation.memberNodeIds == null)
                {
                    selected.RemoveAt(i);
                    continue;
                }

                bool conflicts = false;
                for (int j = 0; j < relation.memberNodeIds.Length; j++)
                {
                    if (!claimedMembers.Add(relation.memberNodeIds[j]))
                    {
                        conflicts = true;
                        break;
                    }
                }

                if (!conflicts)
                {
                    continue;
                }

                for (int j = 0; j < relation.memberNodeIds.Length; j++)
                {
                    claimedMembers.Remove(relation.memberNodeIds[j]);
                }
                selected.RemoveAt(i);
            }

            document = new AiChildRelationResultDocument
            {
                version = AiProtocolVersions.ChildRelation,
                treeHash = package.treeHash ?? string.Empty,
                relations = selected
            };
            return true;
        }

        private static bool TryCanonicalizeRelation(AiChildRelationEntry entry, Dictionary<string, AiAnalysisNodeEntry> nodeById, out AiChildRelationEntry canonical)
        {
            canonical = null;
            if (entry == null
                || string.IsNullOrWhiteSpace(entry.ownerId)
                || nodeById == null
                || !nodeById.ContainsKey(entry.ownerId)
                || !AiHelperContractCatalog.TryParseRoleType(entry.roleType, out var roleType))
            {
                return false;
            }

            var members = NormalizeMemberNodeIds(entry.memberNodeIds, NormalizeExistingNodeId(entry.targetId, nodeById), nodeById);
            if (members.Length < 1)
            {
                return false;
            }

            canonical = new AiChildRelationEntry
            {
                ownerId = entry.ownerId.Trim(),
                targetId = NormalizeExistingNodeId(entry.targetId, nodeById),
                roleType = roleType.ToString(),
                memberNodeIds = members,
                confidence = NormalizeConfidence(entry.confidence),
                reason = NormalizeReason(entry.reason, "AI 子控件归属识别结果。")
            };
            return true;
        }

        private static Dictionary<string, AiAnalysisNodeEntry> BuildNodeMap(List<AiAnalysisNodeEntry> nodes)
        {
            var result = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.OrdinalIgnoreCase);
            if (nodes == null)
            {
                return result;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node != null && !string.IsNullOrWhiteSpace(node.id))
                {
                    result[node.id] = node;
                }
            }

            return result;
        }

        private static Dictionary<string, GUIType> BuildCurrentTypeMap(List<AiAnalysisNodeEntry> nodes)
        {
            var result = new Dictionary<string, GUIType>(StringComparer.OrdinalIgnoreCase);
            if (nodes == null)
            {
                return result;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                result[node.id] = ResolveFallbackCurrentType(node);
            }

            return result;
        }

        private static Dictionary<string, GUIType> BuildOwnerCarrierTypeMap(AiRecognitionCombinedResultDocument combined)
        {
            var result = new Dictionary<string, GUIType>(StringComparer.OrdinalIgnoreCase);
            if (combined == null || combined.owners == null)
            {
                return result;
            }

            for (int i = 0; i < combined.owners.Count; i++)
            {
                var owner = combined.owners[i];
                if (owner == null || string.IsNullOrWhiteSpace(owner.carrierNodeId) || !AiHelperContractCatalog.TryParseOwnerType(owner.ownerType, out var ownerType))
                {
                    continue;
                }

                result[owner.carrierNodeId] = ownerType;
            }

            return result;
        }

        private static Dictionary<string, string> BuildOwnerCarrierNodeIdMap(AiRecognitionCombinedResultDocument combined)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (combined == null || combined.owners == null)
            {
                return result;
            }

            for (int i = 0; i < combined.owners.Count; i++)
            {
                var owner = combined.owners[i];
                if (owner == null || string.IsNullOrWhiteSpace(owner.ownerId) || string.IsNullOrWhiteSpace(owner.carrierNodeId))
                {
                    continue;
                }

                result[owner.ownerId] = owner.carrierNodeId;
            }

            return result;
        }

        private static string NormalizeExistingNodeId(string value, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (string.IsNullOrWhiteSpace(value) || nodeById == null || !nodeById.ContainsKey(value.Trim()))
            {
                return string.Empty;
            }

            return value.Trim();
        }

        private static string[] NormalizeMemberNodeIds(string[] source, string carrierNodeId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            var members = new List<string>(source != null ? source.Length + 1 : 1);
            if (!string.IsNullOrWhiteSpace(carrierNodeId))
            {
                members.Add(carrierNodeId);
            }

            if (source != null)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    string nodeId = NormalizeExistingNodeId(source[i], nodeById);
                    if (!string.IsNullOrWhiteSpace(nodeId) && !members.Contains(nodeId))
                    {
                        members.Add(nodeId);
                    }
                }
            }

            return members.ToArray();
        }

        private static bool TryResolveSingleTextMember(string[] memberNodeIds, string carrierNodeId, Dictionary<string, AiAnalysisNodeEntry> nodeById, out string textNodeId)
        {
            textNodeId = string.Empty;
            if (!string.IsNullOrWhiteSpace(carrierNodeId)
                && nodeById.TryGetValue(carrierNodeId, out var carrierNode)
                && carrierNode != null
                && carrierNode.isTextLayer)
            {
                textNodeId = carrierNodeId;
                return true;
            }

            if (memberNodeIds == null || memberNodeIds.Length != 1)
            {
                return false;
            }

            string memberId = memberNodeIds[0];
            if (nodeById.TryGetValue(memberId, out var memberNode) && memberNode != null && memberNode.isTextLayer)
            {
                textNodeId = memberId;
                return true;
            }

            return false;
        }

        private static int GetOwnerScore(AiRecognitionOwnerEntry entry)
        {
            if (entry == null)
            {
                return int.MinValue;
            }

            int score = (int)(entry.confidence * 100f);
            if (!string.IsNullOrWhiteSpace(entry.carrierNodeId))
            {
                score += 20;
            }
            if (entry.memberNodeIds != null)
            {
                score += entry.memberNodeIds.Length == 1 ? 10 : 5;
            }
            return score;
        }

        private static int GetRoleScore(AiRecognitionRoleEntry entry)
        {
            if (entry == null)
            {
                return int.MinValue;
            }

            int score = (int)(entry.confidence * 100f);
            if (!string.IsNullOrWhiteSpace(entry.carrierNodeId))
            {
                score += 25;
            }
            if (entry.memberNodeIds != null)
            {
                score += entry.memberNodeIds.Length == 1 ? 20 : 5;
            }
            if (AiHelperContractCatalog.TryParseRoleType(entry.roleType, out var roleType) && AiHelperContractCatalog.IsTextRole(roleType))
            {
                score += 10;
            }
            return score;
        }

        private static int GetLegacyRelationScore(AiChildRelationEntry entry)
        {
            if (entry == null)
            {
                return int.MinValue;
            }

            int score = (int)(entry.confidence * 100f);
            if (!string.IsNullOrWhiteSpace(entry.targetId))
            {
                score += 20;
            }
            if (entry.memberNodeIds != null)
            {
                score += entry.memberNodeIds.Length == 1 ? 10 : 5;
            }
            return score;
        }

        private static GUIType ParseOwnerType(string value)
        {
            return AiHelperContractCatalog.TryParseOwnerType(value, out var ownerType)
                ? ownerType
                : GUIType.Null;
        }

        private static bool TryResolveOwnerType(
            string value,
            string carrierNodeId,
            string[] memberNodeIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out GUIType ownerType)
        {
            if (AiHelperContractCatalog.TryParseOwnerType(value, out ownerType))
            {
                ownerType = AiHelperContractCatalog.NormalizeAiType(ownerType);
                return true;
            }

            if (AiHelperContractCatalog.TryParseRoleType(value, out var roleType))
            {
                if (roleType == GUIType.Background)
                {
                    ownerType = InferFallbackOwnerType(carrierNodeId, memberNodeIds, nodeById);
                    return ownerType != GUIType.Null;
                }

                if (AiHelperContractCatalog.IsTextRole(roleType)
                    && TryResolveSingleTextMember(memberNodeIds, carrierNodeId, nodeById, out _))
                {
                    ownerType = GUIType.Text;
                    return true;
                }
            }

            ownerType = GUIType.Null;
            return false;
        }

        private static bool TryResolveRoleType(
            string value,
            GUIType ownerType,
            string carrierNodeId,
            string[] memberNodeIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out GUIType roleType)
        {
            if (AiHelperContractCatalog.TryParseRoleType(value, out roleType))
            {
                roleType = AiHelperContractCatalog.NormalizeAiType(roleType);
                return true;
            }

            if (!AiPatchValidator.TryParseUIType(value, out var parsedType))
            {
                roleType = GUIType.Null;
                return false;
            }

            parsedType = AiHelperContractCatalog.NormalizeAiType(parsedType);
            if (parsedType == GUIType.Text)
            {
                if (TryGetPreferredTextRole(ownerType, out roleType)
                    && TryResolveSingleTextMember(memberNodeIds, carrierNodeId, nodeById, out _))
                {
                    return true;
                }

                roleType = GUIType.Null;
                return false;
            }

            if (AiHelperContractCatalog.AllowsRole(ownerType, GUIType.Background))
            {
                roleType = GUIType.Background;
                return true;
            }

            roleType = GUIType.Null;
            return false;
        }

        private static GUIType ResolveNodeLabelType(string value, AiAnalysisNodeEntry node)
        {
            if (AiHelperContractCatalog.TryParseNodeLabelType(value, out var labelType))
            {
                return labelType;
            }

            if (AiPatchValidator.TryParseUIType(value, out var parsedType))
            {
                parsedType = AiHelperContractCatalog.NormalizeAiType(parsedType);
                if (parsedType == GUIType.Text && node != null && node.isTextLayer)
                {
                    return GUIType.Text;
                }
            }

            return AiHelperContractCatalog.ResolveSafeNodeLabelFallback(node);
        }

        private static GUIType InferFallbackOwnerType(
            string carrierNodeId,
            string[] memberNodeIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            bool hasText = false;
            bool hasVisual = false;

            if (!string.IsNullOrWhiteSpace(carrierNodeId)
                && nodeById.TryGetValue(carrierNodeId, out var carrierNode)
                && carrierNode != null)
            {
                hasText |= carrierNode.isTextLayer;
                hasVisual |= !carrierNode.isTextLayer;
            }

            if (memberNodeIds != null)
            {
                for (int i = 0; i < memberNodeIds.Length; i++)
                {
                    if (!nodeById.TryGetValue(memberNodeIds[i], out var memberNode) || memberNode == null)
                    {
                        continue;
                    }

                    hasText |= memberNode.isTextLayer;
                    hasVisual |= !memberNode.isTextLayer;
                }
            }

            if (hasVisual)
            {
                return GUIType.Image;
            }

            return hasText ? GUIType.Text : GUIType.Null;
        }

        private static bool TryGetPreferredTextRole(GUIType ownerType, out GUIType roleType)
        {
            switch (ownerType)
            {
                case GUIType.Button:
                    roleType = GUIType.Button_Text;
                    return true;
                case GUIType.Dropdown:
                    roleType = GUIType.Dropdown_Label;
                    return true;
                case GUIType.InputField:
                    roleType = GUIType.InputField_Text;
                    return true;
                case GUIType.Toggle:
                    roleType = GUIType.Toggle_Label;
                    return true;
                default:
                    roleType = GUIType.Null;
                    return false;
            }
        }

        private static GUIType ResolveFallbackCurrentType(AiAnalysisNodeEntry node)
        {
            if (node == null)
            {
                return GUIType.Null;
            }

            if (AiPatchValidator.TryParseUIType(node.uiType, out var uiType))
            {
                return AiHelperContractCatalog.NormalizeAiType(uiType);
            }

            return AiHelperContractCatalog.ResolveSafeNodeLabelFallback(node);
        }

        private static string NormalizeCurrentUiTypeString(string value)
        {
            if (!AiPatchValidator.TryParseUIType(value, out var currentType))
            {
                return GUIType.Null.ToString();
            }

            return AiHelperContractCatalog.NormalizeAiType(currentType).ToString();
        }

        private static string NormalizeReason(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static float NormalizeConfidence(float value)
        {
            return value > 0f && value <= 1f ? value : -1f;
        }

        private static float CoerceConfidence(float value)
        {
            float normalized = NormalizeConfidence(value);
            return normalized > 0f ? normalized : 0.5f;
        }

        private static string[] CloneArray(string[] source)
        {
            if (source == null || source.Length < 1)
            {
                return Array.Empty<string>();
            }

            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

    }
}
#endif
