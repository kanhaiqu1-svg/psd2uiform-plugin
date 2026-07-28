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
    internal sealed class AiRecognitionPatchSynthesizer
    {
        private sealed class NodeState
        {
            public AiAnalysisNodeEntry source;
            public GUIType currentType;
            public GUIType finalType;
        }

        private sealed class OwnerRuntime
        {
            public string semanticOwnerId;
            public GUIType ownerType;
            public string existingCarrierNodeId;
            public string runtimeNodeId;
            public string generatedNodeName;
            public string generatedParentId;
            public int generatedInsertIndex;
            public string[] memberNodeIds;
            public string[] memberRootIds;
            public float confidence;
            public string reason;
            public bool requiresGeneratedGroup;
        }

        private sealed class RoleRuntime
        {
            public OwnerRuntime owner;
            public GUIType roleType;
            public string existingCarrierNodeId;
            public string runtimeNodeId;
            public string generatedNodeName;
            public int generatedInsertIndex;
            public string[] memberNodeIds;
            public string[] memberRootIds;
            public float confidence;
            public string reason;
            public bool requiresGeneratedGroup;
        }

        private sealed class MovePlan
        {
            public string nodeId;
            public string newParentId;
            public int insertIndex;
            public float confidence;
            public string reason;
        }

        internal bool TryBuildPatch(
            AiAnalysisPackageDocument package,
            AiRecognitionCombinedResultDocument combined,
            out AiPatchDocument patch,
            out string error)
        {
            patch = null;
            error = null;
            if (package == null || package.nodes == null || combined == null || combined.nodeLabels == null)
            {
                error = "Recognition synthesis input is invalid.";
                return false;
            }

            var nodeById = BuildNodeMap(package.nodes);
            var nodeStates = BuildNodeStates(package.nodes, combined.nodeLabels);

            if (!TryBuildOwners(combined.owners, nodeById, out var owners, out error))
            {
                return false;
            }

            if (!TryBuildRoles(combined.roles, owners, nodeById, out var roles, out error))
            {
                return false;
            }

            ApplyStructureNormalization(nodeById, owners, roles);
            ApplyOwnerAndRoleTypes(nodeStates, nodeById, owners, roles);
            var movePlans = BuildMovePlans(nodeById, owners, roles);

            patch = new AiPatchDocument
            {
                version = AiProtocolVersions.Patch,
                treeHash = package.treeHash ?? string.Empty,
                analysis = new List<AiAuditEntry>((owners != null ? owners.Count : 0) + (roles != null ? roles.Count : 0)),
                operations = new List<AiPatchOperation>(64)
            };

            BuildCreateGroupOperations(patch.operations, owners, roles);
            BuildMoveOperations(patch.operations, movePlans);
            BuildTypeOperations(patch.operations, nodeStates, owners, roles);
            BuildAnalysisEntries(patch.analysis, nodeStates, owners, roles);
            AiPatchValidator.NormalizeOperationOrder(patch);
            return true;
        }

        internal bool TryBuildPatch(
            AiAnalysisPackageDocument package,
            AiMainTypeResultDocument mainTypes,
            AiChildRelationResultDocument childRelations,
            out AiPatchDocument patch,
            out string error)
        {
            patch = null;
            error = null;
            if (package == null || mainTypes == null || childRelations == null)
            {
                error = "Legacy recognition synthesis input is invalid.";
                return false;
            }

            var combined = new AiRecognitionCombinedResultDocument
            {
                version = AiProtocolVersions.RecognitionCombined,
                treeHash = package.treeHash ?? string.Empty,
                owners = new List<AiRecognitionOwnerEntry>(mainTypes.nodes != null ? mainTypes.nodes.Count : 0),
                roles = new List<AiRecognitionRoleEntry>(childRelations.relations != null ? childRelations.relations.Count : 0),
                nodeLabels = new List<AiRecognitionNodeLabelEntry>(package.nodes != null ? package.nodes.Count : 0)
            };

            if (package.nodes != null)
            {
                for (int i = 0; i < package.nodes.Count; i++)
                {
                    var node = package.nodes[i];
                    if (node == null || string.IsNullOrWhiteSpace(node.id))
                    {
                        continue;
                    }

                    GUIType safeLabel = AiHelperContractCatalog.ResolveSafeNodeLabelFallback(node);
                    if (mainTypes.nodes != null)
                    {
                        for (int j = 0; j < mainTypes.nodes.Count; j++)
                        {
                            var entry = mainTypes.nodes[j];
                            if (entry == null || !string.Equals(entry.targetId, node.id, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (AiHelperContractCatalog.TryParseOwnerType(entry.predictedUIType, out var ownerType))
                            {
                                ownerType = AiHelperContractCatalog.NormalizeAiType(ownerType);
                                if (ownerType == GUIType.Image || ownerType == GUIType.Text || ownerType == GUIType.Mask || ownerType == GUIType.FillColor || ownerType == GUIType.Panel || ownerType == GUIType.Null)
                                {
                                    safeLabel = ownerType;
                                }
                            }

                            break;
                        }
                    }

                    combined.nodeLabels.Add(new AiRecognitionNodeLabelEntry
                    {
                        nodeId = node.id,
                        currentUIType = NormalizeCurrentType(node).ToString(),
                        labelType = safeLabel.ToString(),
                        confidence = 0.9f,
                        reason = "旧版识别结果升级的 nodeLabel。"
                    });
                }
            }

            if (mainTypes.nodes != null)
            {
                for (int i = 0; i < mainTypes.nodes.Count; i++)
                {
                    var entry = mainTypes.nodes[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.targetId) || !AiHelperContractCatalog.TryParseOwnerType(entry.predictedUIType, out var ownerType))
                    {
                        continue;
                    }

                    combined.owners.Add(new AiRecognitionOwnerEntry
                    {
                        ownerId = entry.targetId,
                        ownerType = ownerType.ToString(),
                        carrierNodeId = entry.targetId,
                        memberNodeIds = new[] { entry.targetId },
                        confidence = NormalizeConfidence(entry.confidence),
                        reason = entry.reason ?? "旧版主类型识别结果升级的 owner。"
                    });
                }
            }

            if (childRelations.relations != null)
            {
                for (int i = 0; i < childRelations.relations.Count; i++)
                {
                    var relation = childRelations.relations[i];
                    if (relation == null)
                    {
                        continue;
                    }

                    combined.roles.Add(new AiRecognitionRoleEntry
                    {
                        ownerId = relation.ownerId ?? string.Empty,
                        roleType = relation.roleType ?? string.Empty,
                        carrierNodeId = relation.targetId ?? string.Empty,
                        memberNodeIds = CloneArray(relation.memberNodeIds),
                        confidence = NormalizeConfidence(relation.confidence),
                        reason = relation.reason ?? "旧版子控件关系升级的 role。"
                    });
                }
            }

            return TryBuildPatch(package, combined, out patch, out error);
        }

        private static Dictionary<string, NodeState> BuildNodeStates(List<AiAnalysisNodeEntry> packageNodes, List<AiRecognitionNodeLabelEntry> labels)
        {
            var result = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
            var labelByNodeId = new Dictionary<string, AiRecognitionNodeLabelEntry>(StringComparer.OrdinalIgnoreCase);
            if (labels != null)
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    var label = labels[i];
                    if (label != null && !string.IsNullOrWhiteSpace(label.nodeId))
                    {
                        labelByNodeId[label.nodeId] = label;
                    }
                }
            }

            if (packageNodes == null)
            {
                return result;
            }

            for (int i = 0; i < packageNodes.Count; i++)
            {
                var node = packageNodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                GUIType currentType = NormalizeCurrentType(node);
                GUIType labelType = labelByNodeId.TryGetValue(node.id, out var label) && label != null && AiHelperContractCatalog.TryParseNodeLabelType(label.labelType, out var parsedLabelType)
                    ? parsedLabelType
                    : AiHelperContractCatalog.ResolveSafeNodeLabelFallback(node);

                result[node.id] = new NodeState
                {
                    source = node,
                    currentType = currentType,
                    finalType = labelType
                };
            }

            return result;
        }

        private static bool TryBuildOwners(
            List<AiRecognitionOwnerEntry> sourceOwners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out List<OwnerRuntime> owners,
            out string error)
        {
            owners = new List<OwnerRuntime>(sourceOwners != null ? sourceOwners.Count : 0);
            error = null;
            if (sourceOwners == null)
            {
                return true;
            }

            for (int i = 0; i < sourceOwners.Count; i++)
            {
                var entry = sourceOwners[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ownerId) || !AiHelperContractCatalog.TryParseOwnerType(entry.ownerType, out var ownerType))
                {
                    error = $"Owner[{i}] is invalid.";
                    return false;
                }

                var memberRootIds = ReduceToRootNodes(entry.memberNodeIds, nodeById);
                if (memberRootIds.Length < 1)
                {
                    error = $"Owner[{i}] has no valid member roots.";
                    return false;
                }

                string existingCarrierNodeId = ResolveOwnerCarrierNodeId(entry, ownerType, nodeById);
                bool requiresGeneratedGroup = string.IsNullOrWhiteSpace(existingCarrierNodeId);
                string runtimeNodeId = requiresGeneratedGroup
                    ? BuildGeneratedNodeId("gen:owner:", ownerType, entry.ownerId)
                    : existingCarrierNodeId;

                string generatedParentId = requiresGeneratedGroup ? ResolveGeneratedParentId(memberRootIds, nodeById) : string.Empty;
                owners.Add(new OwnerRuntime
                {
                    semanticOwnerId = entry.ownerId,
                    ownerType = ownerType,
                    existingCarrierNodeId = existingCarrierNodeId,
                    runtimeNodeId = runtimeNodeId,
                    generatedNodeName = requiresGeneratedGroup ? BuildGeneratedNodeName("gen_owner", ownerType, entry.ownerId) : string.Empty,
                    generatedParentId = generatedParentId,
                    generatedInsertIndex = requiresGeneratedGroup ? GetMinimumSiblingIndex(memberRootIds, nodeById) : -1,
                    memberNodeIds = CloneArray(entry.memberNodeIds),
                    memberRootIds = memberRootIds,
                    confidence = NormalizeConfidence(entry.confidence),
                    reason = string.IsNullOrWhiteSpace(entry.reason) ? "AI owner 识别结果。" : entry.reason,
                    requiresGeneratedGroup = requiresGeneratedGroup
                });
            }

            return true;
        }

        private static bool TryBuildRoles(
            List<AiRecognitionRoleEntry> sourceRoles,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out List<RoleRuntime> roles,
            out string error)
        {
            roles = new List<RoleRuntime>(sourceRoles != null ? sourceRoles.Count : 0);
            error = null;
            if (sourceRoles == null)
            {
                return true;
            }

            var ownerBySemanticId = new Dictionary<string, OwnerRuntime>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner != null && !string.IsNullOrWhiteSpace(owner.semanticOwnerId))
                {
                    ownerBySemanticId[owner.semanticOwnerId] = owner;
                }
            }

            for (int i = 0; i < sourceRoles.Count; i++)
            {
                var entry = sourceRoles[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ownerId))
                {
                    error = $"Role[{i}] is invalid.";
                    return false;
                }

                if (!ownerBySemanticId.TryGetValue(entry.ownerId, out var owner) || owner == null)
                {
                    error = $"Role[{i}] references unknown owner.";
                    return false;
                }

                if (!AiHelperContractCatalog.TryParseRoleType(entry.roleType, out var roleType))
                {
                    error = $"Role[{i}] has invalid roleType.";
                    return false;
                }

                var memberRootIds = ReduceToRootNodes(entry.memberNodeIds, nodeById);
                if (memberRootIds.Length < 1)
                {
                    error = $"Role[{i}] has no valid member roots.";
                    return false;
                }

                string existingCarrierNodeId = ResolveRoleCarrierNodeId(entry, roleType, memberRootIds, owner, nodeById);
                bool requiresGeneratedGroup = string.IsNullOrWhiteSpace(existingCarrierNodeId);
                string runtimeNodeId = requiresGeneratedGroup
                    ? BuildGeneratedNodeId("gen:role:", roleType, owner.semanticOwnerId + ":" + entry.roleType)
                    : existingCarrierNodeId;

                if (!requiresGeneratedGroup && AiHelperContractCatalog.IsTextRole(roleType))
                {
                    if (!nodeById.TryGetValue(existingCarrierNodeId, out var textNode) || textNode == null || !textNode.isTextLayer)
                    {
                        error = $"Role[{i}] text role carrier is invalid.";
                        return false;
                    }
                }

                roles.Add(new RoleRuntime
                {
                    owner = owner,
                    roleType = roleType,
                    existingCarrierNodeId = existingCarrierNodeId,
                    runtimeNodeId = runtimeNodeId,
                    generatedNodeName = requiresGeneratedGroup ? BuildGeneratedRoleNodeName(roleType, owner, nodeById) : string.Empty,
                    generatedInsertIndex = requiresGeneratedGroup ? GetMinimumSiblingIndex(memberRootIds, nodeById) : -1,
                    memberNodeIds = CloneArray(entry.memberNodeIds),
                    memberRootIds = memberRootIds,
                    confidence = NormalizeConfidence(entry.confidence),
                    reason = string.IsNullOrWhiteSpace(entry.reason) ? "AI role 识别结果。" : entry.reason,
                    requiresGeneratedGroup = requiresGeneratedGroup
                });
            }

            return true;
        }

        private static void ApplyStructureNormalization(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null || roles == null)
            {
                return;
            }

            ResolveRoleCarrierConflicts(nodeById, owners, roles);
        }

        private static void DowngradeInvalidToggleGroups(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner == null || owner.ownerType != GUIType.ToggleGroup)
                {
                    continue;
                }

                bool hasToggleRole = false;
                bool hasToggleChildOwner = false;
                for (int j = 0; j < roles.Count; j++)
                {
                    var role = roles[j];
                    if (role == null || role.owner != owner)
                    {
                        continue;
                    }

                    if (role.roleType == GUIType.Toggle_Checkmark || role.roleType == GUIType.Toggle_Label)
                    {
                        hasToggleRole = true;
                        break;
                    }
                }

                if (!hasToggleRole)
                {
                    for (int j = 0; j < owners.Count; j++)
                    {
                        var childOwner = owners[j];
                        if (childOwner == null
                            || childOwner == owner
                            || childOwner.ownerType != GUIType.Toggle
                            || string.IsNullOrWhiteSpace(childOwner.existingCarrierNodeId))
                        {
                            continue;
                        }

                        if (IsNodeInOwnerScope(childOwner.existingCarrierNodeId, owner, nodeById))
                        {
                            hasToggleChildOwner = true;
                            break;
                        }
                    }
                }

                if (hasToggleRole || hasToggleChildOwner)
                {
                    continue;
                }

                owner.ownerType = GUIType.Null;
                owner.reason = "本地契约修正：无 Toggle 语义支撑，ToggleGroup 降级为 Null 容器。";
                owner.confidence = Math.Max(owner.confidence, 0.92f);

                for (int j = roles.Count - 1; j >= 0; j--)
                {
                    if (roles[j] != null && roles[j].owner == owner)
                    {
                        roles.RemoveAt(j);
                    }
                }
            }
        }

        private static bool HasRole(OwnerRuntime owner, List<RoleRuntime> roles, GUIType roleType)
        {
            return FindRole(owner, roles, roleType) != null;
        }

        private static RoleRuntime FindRole(OwnerRuntime owner, List<RoleRuntime> roles, GUIType roleType)
        {
            if (owner == null || roles == null)
            {
                return null;
            }

            roleType = AiHelperContractCatalog.NormalizeAiType(roleType);
            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role != null && role.owner == owner && role.roleType == roleType)
                {
                    return role;
                }
            }

            return null;
        }

        private static void AddRoleIfMissing(
            List<RoleRuntime> roles,
            OwnerRuntime owner,
            GUIType roleType,
            string carrierNodeId,
            float confidence,
            string reason)
        {
            if (roles == null
                || owner == null
                || string.IsNullOrWhiteSpace(carrierNodeId)
                || FindRole(owner, roles, roleType) != null)
            {
                return;
            }

            roles.Add(new RoleRuntime
            {
                owner = owner,
                roleType = roleType,
                existingCarrierNodeId = carrierNodeId,
                runtimeNodeId = carrierNodeId,
                generatedNodeName = string.Empty,
                generatedInsertIndex = -1,
                memberNodeIds = new[] { carrierNodeId },
                memberRootIds = new[] { carrierNodeId },
                confidence = NormalizeConfidence(confidence),
                reason = reason,
                requiresGeneratedGroup = false
            });
        }

        private static string FindBestButtonTextNodeId(OwnerRuntime owner, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || nodeById == null)
            {
                return string.Empty;
            }

            string bestNodeId = string.Empty;
            int bestScore = int.MinValue;
            int candidateCount = 0;

            foreach (var pair in nodeById)
            {
                var node = pair.Value;
                if (node == null || !node.isTextLayer || !IsNodeInOwnerScope(node.id, owner, nodeById))
                {
                    continue;
                }

                candidateCount++;
                int score = 300;
                if (IsDirectChildCandidate(owner, node.id, nodeById))
                {
                    score += 200;
                }

                if (NodeHasAnyToken(node, "text", "label", "title", "name"))
                {
                    score += 120;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestNodeId = node.id;
                }
            }

            return candidateCount == 1 || bestScore >= 500 ? bestNodeId : string.Empty;
        }

        private static string FindBestRoleCarrierNodeId(
            OwnerRuntime owner,
            GUIType roleType,
            string excludedNodeId,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || nodeById == null)
            {
                return string.Empty;
            }

            string bestNodeId = string.Empty;
            int bestScore = int.MinValue;
            foreach (var pair in nodeById)
            {
                var node = pair.Value;
                if (node == null
                    || string.IsNullOrWhiteSpace(node.id)
                    || string.Equals(node.id, excludedNodeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.id, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase)
                    || !IsNodeInOwnerScope(node.id, owner, nodeById)
                    || !AiHelperContractCatalog.CanReuseRoleCarrier(roleType, node))
                {
                    continue;
                }

                int score = GetRoleCarrierScore(owner, node, roleType, nodeById);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestNodeId = node.id;
                }
            }

            if (bestScore < 300)
            {
                return string.Empty;
            }

            if (TryResolvePreferredRoleCarrierWrapper(bestNodeId, roleType, owner, nodeById, out var wrapperId)
                && !string.IsNullOrWhiteSpace(wrapperId))
            {
                int wrapperScore = nodeById.TryGetValue(wrapperId, out var wrapperNode) && wrapperNode != null
                    ? GetRoleCarrierScore(owner, wrapperNode, roleType, nodeById)
                    : int.MinValue;
                if (wrapperScore >= bestScore)
                {
                    bestNodeId = wrapperId;
                }
            }

            return bestNodeId;
        }

        private static int GetRoleCarrierScore(
            OwnerRuntime owner,
            AiAnalysisNodeEntry node,
            GUIType roleType,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || node == null)
            {
                return int.MinValue;
            }

            int score = 0;
            if (IsDirectChildCandidate(owner, node.id, nodeById))
            {
                score += 220;
            }

            if (node.isGroupLayer)
            {
                score += 120;
            }

            if (node.childCount == 1 || (!string.IsNullOrWhiteSpace(node.onlyChildId) && node.childCount <= 2))
            {
                score += 80;
            }

            switch (roleType)
            {
                case GUIType.Background:
                    if (NodeHasAnyToken(node, "bg", "background", "back", "base", "frame", "levelbg", "barbg", "btn"))
                    {
                        score += 420;
                    }
                    if (NodeHasAnyToken(node, "bar", "fill", "progress", "loading", "exp"))
                    {
                        score -= 160;
                    }
                    break;

                case GUIType.Slider_Fill:
                    if (NodeHasAnyToken(node, "bar", "fill", "progress", "loading", "exp"))
                    {
                        score += 440;
                    }
                    if (NodeHasAnyToken(node, "bg", "background", "levelbg", "barbg"))
                    {
                        score -= 180;
                    }
                    break;

                case GUIType.Slider_Handle:
                    if (NodeHasAnyToken(node, "handle", "thumb", "knob", "arrow"))
                    {
                        score += 460;
                    }
                    break;
            }

            return score;
        }

        private static bool HasStrongSliderOwnerPattern(
            OwnerRuntime owner,
            string backgroundNodeId,
            string fillNodeId,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || string.IsNullOrWhiteSpace(backgroundNodeId) || string.IsNullOrWhiteSpace(fillNodeId))
            {
                return false;
            }

            bool ownerSignal = false;
            if (!string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                && nodeById.TryGetValue(owner.existingCarrierNodeId, out var ownerNode)
                && ownerNode != null)
            {
                ownerSignal = NodeHasAnyToken(ownerNode, "slider", "bar", "progress", "exp", "loading", "level");
            }

            bool backgroundSignal = nodeById.TryGetValue(backgroundNodeId, out var backgroundNode)
                && backgroundNode != null
                && NodeHasAnyToken(backgroundNode, "bg", "background", "back", "base", "levelbg", "barbg");

            bool fillSignal = nodeById.TryGetValue(fillNodeId, out var fillNode)
                && fillNode != null
                && NodeHasAnyToken(fillNode, "bar", "fill", "progress", "loading", "exp");

            if (!backgroundSignal || !fillSignal)
            {
                return false;
            }

            if (ownerSignal)
            {
                return true;
            }

            if (!HasLocalTrackCarrierPattern(owner, backgroundNodeId, fillNodeId, nodeById))
            {
                return false;
            }

            return !HasCompetingDirectInfoChild(owner, backgroundNodeId, fillNodeId, nodeById);
        }

        private static bool HasStrongButtonOwnerPattern(AiAnalysisNodeEntry node, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (node == null || nodeById == null || !node.isGroupLayer)
            {
                return false;
            }

            bool strongName = NodeHasAnyToken(node, "button", "btn", "close", "next", "prev", "play")
                || NodeHasAnyToken(node, "card", "slot", "avatar", "equip", "frame");
            if (!strongName)
            {
                return false;
            }

            if (node.childCount > 6 || node.renderLeafCount > 18)
            {
                return false;
            }

            bool hasBackground = false;
            bool hasText = false;
            bool hasIconLikeImage = false;

            foreach (var pair in nodeById)
            {
                var scopeNode = pair.Value;
                if (scopeNode == null
                    || string.Equals(scopeNode.id, node.id, StringComparison.OrdinalIgnoreCase)
                    || !IsDescendantOrSelfOf(scopeNode.id, node.id, nodeById)
                    || GetHierarchyDistance(scopeNode.id, node.id, nodeById) > 2)
                {
                    continue;
                }

                if (!hasBackground && !scopeNode.isTextLayer && NodeHasAnyToken(scopeNode, "bg", "background", "back", "btn"))
                {
                    hasBackground = true;
                }

                if (!hasText && scopeNode.isTextLayer)
                {
                    hasText = true;
                }

                if (!hasIconLikeImage && !scopeNode.isTextLayer && NodeHasAnyToken(scopeNode, "icon", "flag", "frame"))
                {
                    hasIconLikeImage = true;
                }

                if (hasBackground && (hasText || hasIconLikeImage) && (strongName || NodeHasAnyToken(scopeNode, "btn", "button")))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDirectInteractiveChildOwner(
            OwnerRuntime owner,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            return CountDirectInteractiveChildOwners(owner, owners, nodeById) > 0;
        }

        private static int CountDirectInteractiveChildOwners(
            OwnerRuntime owner,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || owners == null || nodeById == null || string.IsNullOrWhiteSpace(owner.runtimeNodeId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < owners.Count; i++)
            {
                var candidate = owners[i];
                if (candidate == null
                    || candidate == owner
                    || string.IsNullOrWhiteSpace(candidate.existingCarrierNodeId)
                    || !IsInteractiveOwnerType(candidate.ownerType)
                    || !nodeById.TryGetValue(candidate.existingCarrierNodeId, out var candidateNode)
                    || candidateNode == null
                    || !string.Equals(candidateNode.parentId, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool IsInteractiveOwnerType(GUIType ownerType)
        {
            switch (ownerType)
            {
                case GUIType.Button:
                case GUIType.Dropdown:
                case GUIType.InputField:
                case GUIType.Toggle:
                case GUIType.Slider:
                case GUIType.ScrollView:
                case GUIType.ToggleGroup:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasLocalTrackCarrierPattern(
            OwnerRuntime owner,
            string backgroundNodeId,
            string fillNodeId,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || nodeById == null)
            {
                return false;
            }

            bool backgroundDirect = IsDirectChildCandidate(owner, backgroundNodeId, nodeById);
            bool fillDirect = IsDirectChildCandidate(owner, fillNodeId, nodeById);
            if (backgroundDirect && fillDirect)
            {
                return true;
            }

            if (!nodeById.TryGetValue(backgroundNodeId, out var backgroundNode)
                || backgroundNode == null
                || !nodeById.TryGetValue(fillNodeId, out var fillNode)
                || fillNode == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(backgroundNode.parentId)
                && string.Equals(backgroundNode.parentId, fillNode.parentId, StringComparison.OrdinalIgnoreCase)
                && nodeById.TryGetValue(backgroundNode.parentId, out var parentNode)
                && parentNode != null
                && IsNodeInOwnerScope(parentNode.id, owner, nodeById)
                && NodeHasAnyToken(parentNode, "slider", "bar", "progress", "exp", "loading", "track"))
            {
                return true;
            }

            return false;
        }

        private static bool HasCompetingDirectInfoChild(
            OwnerRuntime owner,
            string backgroundNodeId,
            string fillNodeId,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || nodeById == null)
            {
                return false;
            }

            foreach (var pair in nodeById)
            {
                var node = pair.Value;
                if (node == null
                    || !node.isGroupLayer
                    || string.IsNullOrWhiteSpace(node.id)
                    || string.Equals(node.id, backgroundNodeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(node.id, fillNodeId, StringComparison.OrdinalIgnoreCase)
                    || !IsDirectChildCandidate(owner, node.id, nodeById))
                {
                    continue;
                }

                if (LooksLikeStaticInfoPanel(node, nodeById))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePreferredRoleCarrierWrapper(
            string carrierNodeId,
            GUIType roleType,
            OwnerRuntime owner,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out string wrapperId)
        {
            wrapperId = string.Empty;
            if (string.IsNullOrWhiteSpace(carrierNodeId) || owner == null || nodeById == null || AiHelperContractCatalog.IsTextRole(roleType))
            {
                return false;
            }

            string currentId = carrierNodeId;
            string bestId = carrierNodeId;
            while (nodeById.TryGetValue(currentId, out var currentNode)
                && currentNode != null
                && !string.IsNullOrWhiteSpace(currentNode.parentId)
                && nodeById.TryGetValue(currentNode.parentId, out var parentNode)
                && parentNode != null
                && IsNaturalRoleWrapper(parentNode, currentNode, roleType)
                && IsNodeInOwnerScope(parentNode.id, owner, nodeById)
                && !string.Equals(parentNode.id, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase))
            {
                bestId = parentNode.id;
                currentId = parentNode.id;
            }

            wrapperId = bestId;
            return !string.Equals(wrapperId, carrierNodeId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNaturalRoleWrapper(AiAnalysisNodeEntry parentNode, AiAnalysisNodeEntry childNode, GUIType roleType)
        {
            if (parentNode == null || childNode == null || !parentNode.isGroupLayer || parentNode.isTextLayer)
            {
                return false;
            }

            if (parentNode.childCount == 1 || string.Equals(parentNode.onlyChildId, childNode.id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            switch (roleType)
            {
                case GUIType.Background:
                    return NodeHasAnyToken(parentNode, "bg", "background", "back", "base", "frame", "levelbg", "barbg", "btn");
                case GUIType.Slider_Fill:
                    return NodeHasAnyToken(parentNode, "bar", "fill", "progress", "loading", "exp");
                case GUIType.Slider_Handle:
                    return NodeHasAnyToken(parentNode, "handle", "thumb", "knob", "arrow");
                default:
                    return false;
            }
        }

        private static bool IsDescendantOfExistingInteractiveOwner(
            string nodeId,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || owners == null || nodeById == null)
            {
                return false;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner == null
                    || string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                    || owner.ownerType == GUIType.Null
                    || owner.ownerType == GUIType.Panel
                    || owner.ownerType == GUIType.ToggleGroup)
                {
                    continue;
                }

                if (string.Equals(owner.existingCarrierNodeId, nodeId, StringComparison.OrdinalIgnoreCase)
                    || IsDescendantOf(nodeId, owner.existingCarrierNodeId, nodeById))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDirectChildCandidate(
            OwnerRuntime owner,
            string nodeId,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || string.IsNullOrWhiteSpace(nodeId) || nodeById == null || !nodeById.TryGetValue(nodeId, out var node) || node == null)
            {
                return false;
            }

            if (!owner.requiresGeneratedGroup && !string.IsNullOrWhiteSpace(owner.runtimeNodeId))
            {
                return string.Equals(node.parentId, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase);
            }

            for (int i = 0; i < owner.memberRootIds.Length; i++)
            {
                if (string.Equals(owner.memberRootIds[i], nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNodeInOwnerScope(
            string nodeId,
            OwnerRuntime owner,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || owner == null || nodeById == null)
            {
                return false;
            }

            if (!owner.requiresGeneratedGroup && !string.IsNullOrWhiteSpace(owner.existingCarrierNodeId))
            {
                return string.Equals(nodeId, owner.existingCarrierNodeId, StringComparison.OrdinalIgnoreCase)
                    || IsDescendantOf(nodeId, owner.existingCarrierNodeId, nodeById);
            }

            for (int i = 0; i < owner.memberRootIds.Length; i++)
            {
                string rootId = owner.memberRootIds[i];
                if (string.Equals(nodeId, rootId, StringComparison.OrdinalIgnoreCase) || IsDescendantOf(nodeId, rootId, nodeById))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeHasAnyToken(AiAnalysisNodeEntry node, params string[] expectedTokens)
        {
            if (node == null || expectedTokens == null || expectedTokens.Length < 1)
            {
                return false;
            }

            if (node.nameTokens != null)
            {
                for (int i = 0; i < node.nameTokens.Length; i++)
                {
                    string token = node.nameTokens[i];
                    if (MatchesAnyToken(token, expectedTokens))
                    {
                        return true;
                    }
                }
            }

            return MatchesAnyToken(node.name, expectedTokens) || MatchesAnyToken(node.layerName, expectedTokens);
        }

        private static bool MatchesAnyToken(string value, string[] expectedTokens)
        {
            if (string.IsNullOrWhiteSpace(value) || expectedTokens == null)
            {
                return false;
            }

            for (int i = 0; i < expectedTokens.Length; i++)
            {
                string expected = expectedTokens[i];
                if (!string.IsNullOrWhiteSpace(expected) && value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void InferMissingButtonOwners(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            Dictionary<string, NodeState> nodeStates,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null)
            {
                return;
            }

            var claimedOwnerNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner != null && !string.IsNullOrWhiteSpace(owner.existingCarrierNodeId))
                {
                    claimedOwnerNodes.Add(owner.existingCarrierNodeId);
                }
            }

            var candidates = new List<AiAnalysisNodeEntry>(nodeById.Values);
            candidates.Sort((left, right) => GetSiblingIndex(nodeById, left != null ? left.id : null).CompareTo(GetSiblingIndex(nodeById, right != null ? right.id : null)));

            for (int i = 0; i < candidates.Count; i++)
            {
                var node = candidates[i];
                if (node == null
                    || !node.isGroupLayer
                    || string.IsNullOrWhiteSpace(node.id)
                    || claimedOwnerNodes.Contains(node.id)
                    || IsDescendantOfExistingInteractiveOwner(node.id, owners, nodeById))
                {
                    continue;
                }

                if (!HasStrongButtonOwnerPattern(node, nodeById))
                {
                    continue;
                }

                if (CountDirectInteractiveChildOwners(
                        new OwnerRuntime
                        {
                            runtimeNodeId = node.id
                        },
                        owners,
                        nodeById) >= 2)
                {
                    continue;
                }

                if (nodeStates != null
                    && nodeStates.TryGetValue(node.id, out var state)
                    && state != null
                    && state.finalType != GUIType.Null
                    && state.finalType != GUIType.Panel)
                {
                    continue;
                }

                var owner = new OwnerRuntime
                {
                    semanticOwnerId = node.id,
                    ownerType = GUIType.Button,
                    existingCarrierNodeId = node.id,
                    runtimeNodeId = node.id,
                    generatedNodeName = string.Empty,
                    generatedParentId = string.Empty,
                    generatedInsertIndex = -1,
                    memberNodeIds = new[] { node.id },
                    memberRootIds = new[] { node.id },
                    confidence = 0.9f,
                    reason = "本地契约修正：检测到稳定按钮子树，补建 Button owner。",
                    requiresGeneratedGroup = false
                };

                owners.Add(owner);
                claimedOwnerNodes.Add(node.id);
            }
        }

        private static void DowngradeWeakButtonOwnersToPanels(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner == null
                    || owner.ownerType != GUIType.Button
                    || string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                    || !nodeById.TryGetValue(owner.existingCarrierNodeId, out var ownerNode)
                    || ownerNode == null
                    || !ownerNode.isGroupLayer)
                {
                    continue;
                }

                if (HasStrongButtonOwnerPattern(ownerNode, nodeById))
                {
                    continue;
                }

                if (!LooksLikeStatusPanelWithInteractiveChild(owner, owners, nodeById))
                {
                    continue;
                }

                owner.ownerType = GUIType.Panel;
                owner.confidence = Math.Max(owner.confidence, 0.93f);
                owner.reason = "本地契约修正：资源/状态信息区含独立子按钮，弱 Button owner 降级为 Panel。";

                for (int j = roles.Count - 1; j >= 0; j--)
                {
                    var role = roles[j];
                    if (role == null || role.owner != owner || role.roleType == GUIType.Background)
                    {
                        continue;
                    }

                    roles.RemoveAt(j);
                }
            }
        }

        private static void InferMissingPanelOwners(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            Dictionary<string, NodeState> nodeStates,
            List<OwnerRuntime> owners)
        {
            if (nodeById == null || owners == null)
            {
                return;
            }

            var claimedOwnerNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner != null && !string.IsNullOrWhiteSpace(owner.existingCarrierNodeId))
                {
                    claimedOwnerNodes.Add(owner.existingCarrierNodeId);
                }
            }

            var candidates = new List<AiAnalysisNodeEntry>(nodeById.Values);
            candidates.Sort((left, right) => GetSiblingIndex(nodeById, left != null ? left.id : null).CompareTo(GetSiblingIndex(nodeById, right != null ? right.id : null)));

            for (int i = 0; i < candidates.Count; i++)
            {
                var node = candidates[i];
                if (node == null
                    || !node.isGroupLayer
                    || string.IsNullOrWhiteSpace(node.id)
                    || claimedOwnerNodes.Contains(node.id))
                {
                    continue;
                }

                if (nodeStates != null
                    && nodeStates.TryGetValue(node.id, out var state)
                    && state != null
                    && state.finalType != GUIType.Null)
                {
                    continue;
                }

                if (!LooksLikeStaticInfoPanel(node, nodeById))
                {
                    continue;
                }

                if (TryFindNearestOwner(node.id, owners, nodeById, out var parentOwner)
                    && parentOwner != null
                    && (parentOwner.ownerType == GUIType.Button
                        || parentOwner.ownerType == GUIType.Toggle
                        || parentOwner.ownerType == GUIType.Dropdown
                        || parentOwner.ownerType == GUIType.InputField))
                {
                    continue;
                }

                owners.Add(new OwnerRuntime
                {
                    semanticOwnerId = node.id,
                    ownerType = GUIType.Panel,
                    existingCarrierNodeId = node.id,
                    runtimeNodeId = node.id,
                    generatedNodeName = string.Empty,
                    generatedParentId = string.Empty,
                    generatedInsertIndex = -1,
                    memberNodeIds = new[] { node.id },
                    memberRootIds = new[] { node.id },
                    confidence = 0.84f,
                    reason = "本地契约修正：检测到稳定的非交互信息子树，补建 Panel owner。",
                    requiresGeneratedGroup = false
                });
                claimedOwnerNodes.Add(node.id);
            }
        }

        private static void PromotePanelOwnersToSliders(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner == null || owner.ownerType != GUIType.Panel || HasRole(owner, roles, GUIType.Slider_Fill))
                {
                    continue;
                }

                string backgroundNodeId = FindBestRoleCarrierNodeId(owner, GUIType.Background, null, nodeById);
                string fillNodeId = FindBestRoleCarrierNodeId(owner, GUIType.Slider_Fill, backgroundNodeId, nodeById);
                if (string.IsNullOrWhiteSpace(backgroundNodeId) || string.IsNullOrWhiteSpace(fillNodeId))
                {
                    continue;
                }

                if (!HasStrongSliderOwnerPattern(owner, backgroundNodeId, fillNodeId, nodeById))
                {
                    continue;
                }

                owner.ownerType = GUIType.Slider;
                owner.confidence = Math.Max(owner.confidence, 0.94f);
                owner.reason = "本地契约修正：检测到轨道与填充闭环，Panel 升级为 Slider。";

                AddRoleIfMissing(roles, owner, GUIType.Background, backgroundNodeId, owner.confidence, "本地契约补齐：Slider 的主轨道背景。");
                AddRoleIfMissing(roles, owner, GUIType.Slider_Fill, fillNodeId, owner.confidence, "本地契约补齐：Slider 的填充条。");

                string handleNodeId = FindBestRoleCarrierNodeId(owner, GUIType.Slider_Handle, fillNodeId, nodeById);
                if (!string.IsNullOrWhiteSpace(handleNodeId))
                {
                    AddRoleIfMissing(roles, owner, GUIType.Slider_Handle, handleNodeId, 0.88f, "本地契约补齐：Slider 的可见手柄。");
                }
            }
        }

        private static void EnsureRequiredOwnerRoles(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || owners == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                var owner = owners[i];
                if (owner == null)
                {
                    continue;
                }

                if (owner.ownerType == GUIType.Button)
                {
                    if (!HasRole(owner, roles, GUIType.Background))
                    {
                        string backgroundNodeId = FindBestRoleCarrierNodeId(owner, GUIType.Background, null, nodeById);
                        if (!string.IsNullOrWhiteSpace(backgroundNodeId))
                        {
                            AddRoleIfMissing(roles, owner, GUIType.Background, backgroundNodeId, 0.9f, "本地契约补齐：Button 的主背景。");
                        }
                    }

                    if (!HasRole(owner, roles, GUIType.Button_Text))
                    {
                        string textNodeId = FindBestButtonTextNodeId(owner, nodeById);
                        if (!string.IsNullOrWhiteSpace(textNodeId))
                        {
                            AddRoleIfMissing(roles, owner, GUIType.Button_Text, textNodeId, 0.88f, "本地契约补齐：Button 的主标题文字。");
                        }
                    }
                }
                else if (owner.ownerType == GUIType.Panel || owner.ownerType == GUIType.ToggleGroup)
                {
                    if (!HasRole(owner, roles, GUIType.Background))
                    {
                        string backgroundNodeId = FindBestRoleCarrierNodeId(owner, GUIType.Background, null, nodeById);
                        if (!string.IsNullOrWhiteSpace(backgroundNodeId))
                        {
                            AddRoleIfMissing(roles, owner, GUIType.Background, backgroundNodeId, 0.82f, "本地契约补齐：容器的唯一底板。");
                        }
                    }
                }
            }
        }

        private static bool LooksLikeStaticInfoPanel(AiAnalysisNodeEntry node, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (node == null || nodeById == null || !node.isGroupLayer)
            {
                return false;
            }

            if (node.childCount < 1 || node.childCount > 5 || node.renderLeafCount < 2 || node.renderLeafCount > 12)
            {
                return false;
            }

            if (NodeHasAnyToken(node, "btn", "button", "play", "next", "prev", "close", "slider", "fill", "handle", "toggle", "dropdown", "input"))
            {
                return false;
            }

            bool strongPanelName = NodeHasAnyToken(node, "level", "num", "value", "badge", "info", "status");
            bool hasText = false;
            bool hasBackgroundLike = false;
            bool hasSliderLike = false;

            foreach (var pair in nodeById)
            {
                var scopeNode = pair.Value;
                if (scopeNode == null
                    || string.Equals(scopeNode.id, node.id, StringComparison.OrdinalIgnoreCase)
                    || !IsDescendantOrSelfOf(scopeNode.id, node.id, nodeById)
                    || GetHierarchyDistance(scopeNode.id, node.id, nodeById) > 3)
                {
                    continue;
                }

                if (!hasText && scopeNode.isTextLayer)
                {
                    hasText = true;
                }

                if (!hasBackgroundLike && !scopeNode.isTextLayer && NodeHasAnyToken(scopeNode, "bg", "background", "back", "base", "frame", "levelbg"))
                {
                    hasBackgroundLike = true;
                }

                if (!hasSliderLike && NodeHasAnyToken(scopeNode, "fill", "bar", "progress", "loading", "exp", "handle"))
                {
                    hasSliderLike = true;
                }
            }

            return strongPanelName && hasText && hasBackgroundLike && !hasSliderLike;
        }

        private static bool LooksLikeStatusPanelWithInteractiveChild(
            OwnerRuntime owner,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null
                || owners == null
                || nodeById == null
                || string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                || !nodeById.TryGetValue(owner.existingCarrierNodeId, out var ownerNode)
                || ownerNode == null
                || !ownerNode.isGroupLayer)
            {
                return false;
            }

            if (ownerNode.childCount < 2 || ownerNode.childCount > 6 || ownerNode.renderLeafCount < 3 || ownerNode.renderLeafCount > 12)
            {
                return false;
            }

            if (!HasDirectInteractiveChildOwner(owner, owners, nodeById))
            {
                return false;
            }

            if (NodeHasAnyToken(ownerNode, "button", "btn", "play", "next", "prev", "close"))
            {
                return false;
            }

            bool hasText = false;
            bool hasBackgroundLike = false;
            bool hasIconLike = false;

            foreach (var pair in nodeById)
            {
                var scopeNode = pair.Value;
                if (scopeNode == null
                    || string.Equals(scopeNode.id, ownerNode.id, StringComparison.OrdinalIgnoreCase)
                    || !IsDescendantOrSelfOf(scopeNode.id, ownerNode.id, nodeById)
                    || GetHierarchyDistance(scopeNode.id, ownerNode.id, nodeById) > 3)
                {
                    continue;
                }

                if (!hasText && scopeNode.isTextLayer)
                {
                    hasText = true;
                }

                if (!hasBackgroundLike && !scopeNode.isTextLayer && NodeHasAnyToken(scopeNode, "bg", "background", "back", "base", "frame", "status"))
                {
                    hasBackgroundLike = true;
                }

                if (!hasIconLike && !scopeNode.isTextLayer && NodeHasAnyToken(scopeNode, "icon", "gem", "gold", "coin", "energy", "flag", "resource", "currency"))
                {
                    hasIconLike = true;
                }
            }

            return hasText && hasBackgroundLike && hasIconLike;
        }

        private static bool TryFindNearestOwner(
            string nodeId,
            List<OwnerRuntime> owners,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out OwnerRuntime owner)
        {
            owner = null;
            if (string.IsNullOrWhiteSpace(nodeId) || owners == null || nodeById == null)
            {
                return false;
            }

            OwnerRuntime bestOwner = null;
            int bestDepth = int.MaxValue;
            for (int i = 0; i < owners.Count; i++)
            {
                var candidate = owners[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.existingCarrierNodeId))
                {
                    continue;
                }

                if (!IsDescendantOf(nodeId, candidate.existingCarrierNodeId, nodeById))
                {
                    continue;
                }

                int depth = GetHierarchyDistance(nodeId, candidate.existingCarrierNodeId, nodeById);
                if (depth >= 0 && depth < bestDepth)
                {
                    bestDepth = depth;
                    bestOwner = candidate;
                }
            }

            owner = bestOwner;
            return owner != null;
        }

        private static void PromoteRoleCarriersToNaturalWrappers(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null || role.requiresGeneratedGroup || string.IsNullOrWhiteSpace(role.existingCarrierNodeId) || AiHelperContractCatalog.IsTextRole(role.roleType))
                {
                    continue;
                }

                if (!TryResolvePreferredRoleCarrierWrapper(role.existingCarrierNodeId, role.roleType, role.owner, nodeById, out var preferredCarrierId)
                    || string.IsNullOrWhiteSpace(preferredCarrierId)
                    || string.Equals(preferredCarrierId, role.existingCarrierNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                role.existingCarrierNodeId = preferredCarrierId;
                role.runtimeNodeId = preferredCarrierId;
                role.memberRootIds = new[] { preferredCarrierId };
                role.reason = "本地契约修正：将图片 role 提升到最近自然 wrapper 承载。";
                role.confidence = Math.Max(role.confidence, 0.92f);
            }
        }

        private static void ResolveRoleCarrierConflicts(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || roles == null)
            {
                return;
            }

            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null
                    || role.requiresGeneratedGroup
                    || string.IsNullOrWhiteSpace(role.existingCarrierNodeId)
                    || role.owner == null)
                {
                    continue;
                }

                if (!string.Equals(role.existingCarrierNodeId, role.owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ResolveRoleCarrierConflict(role, null, nodeById);
            }

            var rolesByCarrier = new Dictionary<string, List<RoleRuntime>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null
                    || role.requiresGeneratedGroup
                    || role.owner == null
                    || string.IsNullOrWhiteSpace(role.existingCarrierNodeId))
                {
                    continue;
                }

                string key = role.owner.runtimeNodeId + "\n" + role.existingCarrierNodeId;
                if (!rolesByCarrier.TryGetValue(key, out var bucket))
                {
                    bucket = new List<RoleRuntime>(2);
                    rolesByCarrier.Add(key, bucket);
                }

                bucket.Add(role);
            }

            foreach (var pair in rolesByCarrier)
            {
                var bucket = pair.Value;
                if (bucket == null || bucket.Count < 2)
                {
                    continue;
                }

                bucket.Sort((left, right) =>
                {
                    int leftScore = GetCarrierStickinessScore(left, nodeById);
                    int rightScore = GetCarrierStickinessScore(right, nodeById);
                    return rightScore.CompareTo(leftScore);
                });

                var reservedCarrierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    bucket[0].existingCarrierNodeId
                };

                for (int i = 1; i < bucket.Count; i++)
                {
                    ResolveRoleCarrierConflict(bucket[i], reservedCarrierIds, nodeById);
                    if (!bucket[i].requiresGeneratedGroup && !string.IsNullOrWhiteSpace(bucket[i].existingCarrierNodeId))
                    {
                        reservedCarrierIds.Add(bucket[i].existingCarrierNodeId);
                    }
                }
            }
        }

        private static int GetCarrierStickinessScore(RoleRuntime role, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (role == null
                || role.owner == null
                || nodeById == null
                || string.IsNullOrWhiteSpace(role.existingCarrierNodeId)
                || !nodeById.TryGetValue(role.existingCarrierNodeId, out var carrierNode)
                || carrierNode == null)
            {
                return int.MinValue;
            }

            return GetRoleCarrierScore(role.owner, carrierNode, role.roleType, nodeById);
        }

        private static void ResolveRoleCarrierConflict(
            RoleRuntime role,
            HashSet<string> forbiddenCarrierIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (role == null || role.owner == null || nodeById == null)
            {
                return;
            }

            string[] candidateRootIds = GetConflictSafeRoleRootIds(role, nodeById);

            if (TryFindDistinctRoleCarrierNodeId(role, candidateRootIds, forbiddenCarrierIds, nodeById, out var distinctCarrierId))
            {
                role.existingCarrierNodeId = distinctCarrierId;
                role.runtimeNodeId = distinctCarrierId;
                role.memberRootIds = candidateRootIds != null && candidateRootIds.Length > 0
                    ? candidateRootIds
                    : new[] { distinctCarrierId };
                role.reason = "本地契约修正：role carrier 与 owner/其他 role 冲突，回退到更具体的独立承载层。";
                role.confidence = Math.Max(role.confidence, 0.93f);
                role.requiresGeneratedGroup = false;
                return;
            }

            if (candidateRootIds != null && candidateRootIds.Length > 1)
            {
                role.existingCarrierNodeId = string.Empty;
                role.runtimeNodeId = BuildGeneratedNodeId("gen:role:", role.roleType, role.owner.semanticOwnerId + ":" + role.roleType);
                role.generatedNodeName = BuildGeneratedRoleNodeName(role.roleType, role.owner, nodeById);
                role.memberRootIds = candidateRootIds;
                role.generatedInsertIndex = GetMinimumSiblingIndex(candidateRootIds, nodeById);
                role.requiresGeneratedGroup = true;
                role.reason = "本地契约修正：role 无法与 owner/其他 role 共用同一 carrier，改为生成独立 role 节点。";
                role.confidence = Math.Max(role.confidence, 0.93f);
            }
        }

        private static bool TryFindDistinctRoleCarrierNodeId(
            RoleRuntime role,
            string[] candidateRootIds,
            HashSet<string> forbiddenCarrierIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out string carrierNodeId)
        {
            carrierNodeId = string.Empty;
            if (role == null || role.owner == null || nodeById == null)
            {
                return false;
            }

            if (candidateRootIds == null || candidateRootIds.Length < 1)
            {
                return false;
            }

            if (candidateRootIds.Length == 1)
            {
                string rootId = candidateRootIds[0];
                if (!IsForbiddenRoleCarrier(rootId, role.owner, forbiddenCarrierIds)
                    && nodeById.TryGetValue(rootId, out var rootNode)
                    && rootNode != null
                    && AiHelperContractCatalog.CanReuseRoleCarrier(role.roleType, rootNode))
                {
                    carrierNodeId = rootId;
                    return true;
                }

                if (TryResolvePreferredDistinctRoleWrapper(rootId, role, forbiddenCarrierIds, nodeById, out var wrapperId))
                {
                    carrierNodeId = wrapperId;
                    return true;
                }

                return false;
            }

            if (TryResolveDistinctCommonWrapper(candidateRootIds, role.owner, forbiddenCarrierIds, nodeById, out var commonWrapperId))
            {
                carrierNodeId = commonWrapperId;
                return true;
            }

            return false;
        }

        private static string[] GetConflictSafeRoleRootIds(RoleRuntime role, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (role == null || role.owner == null || nodeById == null)
            {
                return Array.Empty<string>();
            }

            if (role.memberNodeIds == null || role.memberNodeIds.Length < 1)
            {
                return role.memberRootIds ?? Array.Empty<string>();
            }

            var candidateIds = new List<string>(role.memberNodeIds.Length);
            for (int i = 0; i < role.memberNodeIds.Length; i++)
            {
                string nodeId = role.memberNodeIds[i];
                if (string.IsNullOrWhiteSpace(nodeId)
                    || string.Equals(nodeId, role.owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase)
                    || !nodeById.ContainsKey(nodeId)
                    || !IsNodeInOwnerScope(nodeId, role.owner, nodeById))
                {
                    continue;
                }

                candidateIds.Add(nodeId);
            }

            string[] reduced = ReduceToRootNodes(candidateIds.ToArray(), nodeById);
            if (reduced.Length > 0)
            {
                return reduced;
            }

            return role.memberRootIds ?? Array.Empty<string>();
        }

        private static bool TryResolvePreferredDistinctRoleWrapper(
            string carrierNodeId,
            RoleRuntime role,
            HashSet<string> forbiddenCarrierIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out string wrapperId)
        {
            wrapperId = string.Empty;
            if (string.IsNullOrWhiteSpace(carrierNodeId)
                || role == null
                || role.owner == null
                || nodeById == null
                || AiHelperContractCatalog.IsTextRole(role.roleType))
            {
                return false;
            }

            string currentId = carrierNodeId;
            string bestId = string.Empty;
            int bestScore = int.MinValue;
            while (nodeById.TryGetValue(currentId, out var currentNode)
                && currentNode != null
                && !string.IsNullOrWhiteSpace(currentNode.parentId)
                && nodeById.TryGetValue(currentNode.parentId, out var parentNode)
                && parentNode != null
                && IsNaturalRoleWrapper(parentNode, currentNode, role.roleType)
                && IsNodeInOwnerScope(parentNode.id, role.owner, nodeById)
                && !IsForbiddenRoleCarrier(parentNode.id, role.owner, forbiddenCarrierIds))
            {
                int score = GetRoleCarrierScore(role.owner, parentNode, role.roleType, nodeById);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = parentNode.id;
                }

                currentId = parentNode.id;
            }

            wrapperId = bestId;
            return !string.IsNullOrWhiteSpace(wrapperId);
        }

        private static bool TryResolveDistinctCommonWrapper(
            string[] memberRootIds,
            OwnerRuntime owner,
            HashSet<string> forbiddenCarrierIds,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            out string wrapperId)
        {
            wrapperId = string.Empty;
            if (!TryResolveCommonWrapper(memberRootIds, owner, nodeById, out var commonWrapperId)
                || IsForbiddenRoleCarrier(commonWrapperId, owner, forbiddenCarrierIds))
            {
                return false;
            }

            wrapperId = commonWrapperId;
            return true;
        }

        private static bool IsForbiddenRoleCarrier(
            string candidateId,
            OwnerRuntime owner,
            HashSet<string> forbiddenCarrierIds)
        {
            if (string.IsNullOrWhiteSpace(candidateId) || owner == null)
            {
                return true;
            }

            if (string.Equals(candidateId, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                    && string.Equals(candidateId, owner.existingCarrierNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return forbiddenCarrierIds != null && forbiddenCarrierIds.Contains(candidateId);
        }

        private static void PromoteNaturalImageWrappers(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            Dictionary<string, NodeState> nodeStates,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeById == null || nodeStates == null)
            {
                return;
            }

            var reservedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner != null && !owner.requiresGeneratedGroup && !string.IsNullOrWhiteSpace(owner.existingCarrierNodeId))
                    {
                        reservedIds.Add(owner.existingCarrierNodeId);
                    }
                }
            }

            if (roles != null)
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    var role = roles[i];
                    if (role != null && !role.requiresGeneratedGroup && !string.IsNullOrWhiteSpace(role.existingCarrierNodeId))
                    {
                        reservedIds.Add(role.existingCarrierNodeId);
                    }
                }
            }

            foreach (var pair in nodeStates)
            {
                var state = pair.Value;
                if (state == null
                    || state.source == null
                    || state.finalType != GUIType.Null
                    || reservedIds.Contains(pair.Key)
                    || !LooksLikeNaturalImageWrapper(state.source, nodeById))
                {
                    continue;
                }

                state.finalType = GUIType.Image;
            }
        }

        private static bool LooksLikeNaturalImageWrapper(AiAnalysisNodeEntry node, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (node == null || nodeById == null || !node.isGroupLayer || node.isTextLayer)
            {
                return false;
            }

            if (node.childCount < 1 || node.childCount > 4 || node.renderLeafCount < 1 || node.renderLeafCount > 6)
            {
                return false;
            }

            if (NodeHasAnyToken(node, "btn", "button", "background", "bg", "text", "label", "title", "bar", "fill", "handle"))
            {
                return false;
            }

            if (!NodeHasAnyToken(node, "icon", "flag", "gem", "coin", "energy", "avatar", "badge", "close"))
            {
                return false;
            }

            bool hasImageLikeDescendant = false;
            foreach (var pair in nodeById)
            {
                var scopeNode = pair.Value;
                if (scopeNode == null
                    || string.Equals(scopeNode.id, node.id, StringComparison.OrdinalIgnoreCase)
                    || !IsDescendantOrSelfOf(scopeNode.id, node.id, nodeById)
                    || GetHierarchyDistance(scopeNode.id, node.id, nodeById) > 3)
                {
                    continue;
                }

                if (scopeNode.isTextLayer)
                {
                    return false;
                }

                hasImageLikeDescendant = true;
            }

            return hasImageLikeDescendant;
        }

        private static void ApplyOwnerAndRoleTypes(
            Dictionary<string, NodeState> nodeStates,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (nodeStates == null)
            {
                return;
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner == null || owner.requiresGeneratedGroup || string.IsNullOrWhiteSpace(owner.existingCarrierNodeId))
                    {
                        continue;
                    }

                    if (nodeStates.TryGetValue(owner.existingCarrierNodeId, out var state) && state != null)
                    {
                        state.finalType = owner.ownerType;
                    }
                }
            }

            if (roles == null)
            {
                return;
            }

            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null || role.requiresGeneratedGroup || string.IsNullOrWhiteSpace(role.existingCarrierNodeId))
                {
                    continue;
                }

                if (nodeStates.TryGetValue(role.existingCarrierNodeId, out var state) && state != null)
                {
                    state.finalType = role.roleType;
                }
            }
        }

        private static List<MovePlan> BuildMovePlans(
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            var finalParentByNodeId = new Dictionary<string, MovePlan>(StringComparer.OrdinalIgnoreCase);
            var claimedByGeneratedRole = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (roles != null)
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    var role = roles[i];
                    if (role == null || !role.requiresGeneratedGroup)
                    {
                        continue;
                    }

                    for (int j = 0; j < role.memberRootIds.Length; j++)
                    {
                        string memberRootId = role.memberRootIds[j];
                        if (string.IsNullOrWhiteSpace(memberRootId) || !claimedByGeneratedRole.Add(memberRootId))
                        {
                            continue;
                        }

                        finalParentByNodeId[memberRootId] = new MovePlan
                        {
                            nodeId = memberRootId,
                            newParentId = role.runtimeNodeId,
                            insertIndex = j,
                            confidence = role.confidence,
                            reason = "多个节点共同构成一个 role，本地移动到生成的 role 节点下。"
                        };
                    }
                }
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner == null || !owner.requiresGeneratedGroup)
                    {
                        continue;
                    }

                    int insertIndex = 0;
                    for (int j = 0; j < owner.memberRootIds.Length; j++)
                    {
                        string memberRootId = owner.memberRootIds[j];
                        if (string.IsNullOrWhiteSpace(memberRootId)
                            || claimedByGeneratedRole.Contains(memberRootId))
                        {
                            continue;
                        }

                        if (!finalParentByNodeId.ContainsKey(memberRootId))
                        {
                            finalParentByNodeId[memberRootId] = new MovePlan
                            {
                                nodeId = memberRootId,
                                newParentId = owner.runtimeNodeId,
                                insertIndex = insertIndex++,
                                confidence = owner.confidence,
                                reason = "owner 由多个节点共同构成，本地移动到生成的 owner 节点下。"
                            };
                        }
                    }
                }
            }

            if (roles != null)
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    var role = roles[i];
                    if (role == null || role.requiresGeneratedGroup || string.IsNullOrWhiteSpace(role.existingCarrierNodeId))
                    {
                        continue;
                    }

                    string carrierRootId = ResolveExistingRoleMoveRoot(role, nodeById);
                    if (string.IsNullOrWhiteSpace(carrierRootId) || finalParentByNodeId.ContainsKey(carrierRootId))
                    {
                        continue;
                    }

                    if (string.Equals(carrierRootId, role.owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsUnderOwnerSubtreeAfterOwnerMove(role.owner, carrierRootId, nodeById))
                    {
                        continue;
                    }

                    finalParentByNodeId[carrierRootId] = new MovePlan
                    {
                        nodeId = carrierRootId,
                        newParentId = role.owner.runtimeNodeId,
                        insertIndex = GetSiblingIndex(nodeById, carrierRootId),
                        confidence = role.confidence,
                        reason = "role 当前不在 owner 语义子树内，本地移动到 owner 下。"
                    };
                }
            }

            var plans = new List<MovePlan>(finalParentByNodeId.Values);
            plans.Sort((left, right) => left.insertIndex.CompareTo(right.insertIndex));
            return plans;
        }

        private static void BuildCreateGroupOperations(List<AiPatchOperation> operations, List<OwnerRuntime> owners, List<RoleRuntime> roles)
        {
            if (operations == null)
            {
                return;
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner == null || !owner.requiresGeneratedGroup)
                    {
                        continue;
                    }

                    operations.Add(new AiPatchOperation
                    {
                        op = AiPatchOperationNames.CreateGroup,
                        id = owner.runtimeNodeId,
                        parentId = owner.generatedParentId,
                        insertIndex = owner.generatedInsertIndex,
                        name = owner.generatedNodeName,
                        uiType = GUIType.Null.ToString(),
                        confidence = owner.confidence,
                        reason = "多个图层共同构成一个 owner，本地补建 owner 节点。"
                    });
                }
            }

            if (roles == null)
            {
                return;
            }

            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null || !role.requiresGeneratedGroup)
                {
                    continue;
                }

                operations.Add(new AiPatchOperation
                {
                    op = AiPatchOperationNames.CreateGroup,
                    id = role.runtimeNodeId,
                    parentId = role.owner.runtimeNodeId,
                    insertIndex = role.generatedInsertIndex,
                    name = role.generatedNodeName,
                    uiType = GUIType.Null.ToString(),
                    confidence = role.confidence,
                    reason = "多个图层共同构成一个 role，本地补建 role 节点。"
                });
            }
        }

        private static void BuildMoveOperations(List<AiPatchOperation> operations, List<MovePlan> movePlans)
        {
            if (operations == null || movePlans == null)
            {
                return;
            }

            for (int i = 0; i < movePlans.Count; i++)
            {
                var move = movePlans[i];
                if (move == null || string.IsNullOrWhiteSpace(move.nodeId) || string.IsNullOrWhiteSpace(move.newParentId))
                {
                    continue;
                }

                operations.Add(new AiPatchOperation
                {
                    op = AiPatchOperationNames.MoveNode,
                    targetId = move.nodeId,
                    newParentId = move.newParentId,
                    insertIndex = move.insertIndex,
                    confidence = move.confidence,
                    reason = move.reason
                });
            }
        }

        private static void BuildTypeOperations(
            List<AiPatchOperation> operations,
            Dictionary<string, NodeState> nodeStates,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (operations == null)
            {
                return;
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner == null || !owner.requiresGeneratedGroup)
                    {
                        continue;
                    }

                    operations.Add(new AiPatchOperation
                    {
                        op = AiPatchOperationNames.SetUIType,
                        targetId = owner.runtimeNodeId,
                        uiType = owner.ownerType.ToString(),
                        confidence = owner.confidence,
                        reason = owner.reason
                    });
                }
            }

            if (roles != null)
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    var role = roles[i];
                    if (role == null || !role.requiresGeneratedGroup)
                    {
                        continue;
                    }

                    operations.Add(new AiPatchOperation
                    {
                        op = AiPatchOperationNames.SetUIType,
                        targetId = role.runtimeNodeId,
                        uiType = role.roleType.ToString(),
                        confidence = role.confidence,
                        reason = role.reason
                    });
                }
            }

            if (nodeStates == null)
            {
                return;
            }

            foreach (var pair in nodeStates)
            {
                var state = pair.Value;
                if (state == null || state.currentType == state.finalType)
                {
                    continue;
                }

                operations.Add(new AiPatchOperation
                {
                    op = AiPatchOperationNames.SetUIType,
                    targetId = pair.Key,
                    uiType = state.finalType.ToString(),
                    confidence = 0.9f,
                    reason = "应用 nodeLabel 与 owner/role 语义图后的最终节点类型。"
                });
            }
        }

        private static void BuildAnalysisEntries(
            List<AiAuditEntry> analysis,
            Dictionary<string, NodeState> nodeStates,
            List<OwnerRuntime> owners,
            List<RoleRuntime> roles)
        {
            if (analysis == null)
            {
                return;
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    var owner = owners[i];
                    if (owner == null)
                    {
                        continue;
                    }

                    analysis.Add(new AiAuditEntry
                    {
                        targetId = owner.runtimeNodeId,
                        ownerId = string.Empty,
                        semanticKind = "owner",
                        currentUIType = ResolveCurrentAuditType(owner.runtimeNodeId, nodeStates),
                        predictedUIType = owner.ownerType.ToString(),
                        verdict = string.Equals(ResolveCurrentAuditType(owner.runtimeNodeId, nodeStates), owner.ownerType.ToString(), StringComparison.Ordinal)
                            ? "correct"
                            : "corrected",
                        confidence = owner.confidence,
                        reason = owner.reason
                    });
                }
            }

            if (roles == null)
            {
                return;
            }

            for (int i = 0; i < roles.Count; i++)
            {
                var role = roles[i];
                if (role == null)
                {
                    continue;
                }

                analysis.Add(new AiAuditEntry
                {
                    targetId = role.runtimeNodeId,
                    ownerId = role.owner != null ? role.owner.runtimeNodeId : string.Empty,
                    semanticKind = "role",
                    currentUIType = ResolveCurrentAuditType(role.runtimeNodeId, nodeStates),
                    predictedUIType = role.roleType.ToString(),
                    verdict = string.Equals(ResolveCurrentAuditType(role.runtimeNodeId, nodeStates), role.roleType.ToString(), StringComparison.Ordinal)
                        ? "correct"
                        : "corrected",
                    confidence = role.confidence,
                    reason = role.reason
                });
            }
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

        private static string ResolveOwnerCarrierNodeId(AiRecognitionOwnerEntry entry, GUIType ownerType, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (entry == null || nodeById == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.carrierNodeId)
                && nodeById.TryGetValue(entry.carrierNodeId, out var carrierNode)
                && carrierNode != null
                && AiHelperContractCatalog.CanReuseOwnerCarrier(ownerType, carrierNode))
            {
                return entry.carrierNodeId;
            }

            if (nodeById.TryGetValue(entry.ownerId, out var ownerNode)
                && ownerNode != null
                && AiHelperContractCatalog.CanReuseOwnerCarrier(ownerType, ownerNode))
            {
                return entry.ownerId;
            }

            return string.Empty;
        }

        private static string ResolveRoleCarrierNodeId(
            AiRecognitionRoleEntry entry,
            GUIType roleType,
            string[] memberRootIds,
            OwnerRuntime owner,
            Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (entry == null || owner == null || nodeById == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.carrierNodeId)
                && nodeById.TryGetValue(entry.carrierNodeId, out var explicitCarrier)
                && explicitCarrier != null
                && AiHelperContractCatalog.CanReuseRoleCarrier(roleType, explicitCarrier))
            {
                return entry.carrierNodeId;
            }

            if (memberRootIds != null && memberRootIds.Length == 1
                && nodeById.TryGetValue(memberRootIds[0], out var singleMember)
                && singleMember != null
                && AiHelperContractCatalog.CanReuseRoleCarrier(roleType, singleMember))
            {
                return memberRootIds[0];
            }

            if (!AiHelperContractCatalog.IsTextRole(roleType))
            {
                if (TryResolveCommonWrapper(memberRootIds, owner, nodeById, out var wrapperId))
                {
                    if (string.Equals(wrapperId, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                            && string.Equals(wrapperId, owner.existingCarrierNodeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        return string.Empty;
                    }

                    return wrapperId;
                }
            }

            return string.Empty;
        }

        private static bool TryResolveCommonWrapper(string[] memberRootIds, OwnerRuntime owner, Dictionary<string, AiAnalysisNodeEntry> nodeById, out string wrapperId)
        {
            wrapperId = string.Empty;
            if (memberRootIds == null || memberRootIds.Length < 2 || owner == null || nodeById == null)
            {
                return false;
            }

            string commonParentId = null;
            for (int i = 0; i < memberRootIds.Length; i++)
            {
                if (!nodeById.TryGetValue(memberRootIds[i], out var memberNode) || memberNode == null || string.IsNullOrWhiteSpace(memberNode.parentId))
                {
                    return false;
                }

                if (commonParentId == null)
                {
                    commonParentId = memberNode.parentId;
                }
                else if (!string.Equals(commonParentId, memberNode.parentId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(commonParentId)
                || !nodeById.TryGetValue(commonParentId, out var parentNode)
                || parentNode == null
                || !parentNode.isGroupLayer)
            {
                return false;
            }

            if (!owner.requiresGeneratedGroup && string.Equals(commonParentId, owner.runtimeNodeId, StringComparison.OrdinalIgnoreCase))
            {
                wrapperId = commonParentId;
                return true;
            }

            if (owner.requiresGeneratedGroup && IsMemberRoot(owner.memberRootIds, commonParentId))
            {
                wrapperId = commonParentId;
                return true;
            }

            return false;
        }

        private static string[] ReduceToRootNodes(string[] nodeIds, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (nodeIds == null || nodeIds.Length < 1 || nodeById == null)
            {
                return Array.Empty<string>();
            }

            var unique = new List<string>(nodeIds.Length);
            for (int i = 0; i < nodeIds.Length; i++)
            {
                string nodeId = nodeIds[i];
                if (!string.IsNullOrWhiteSpace(nodeId) && nodeById.ContainsKey(nodeId) && !unique.Contains(nodeId))
                {
                    unique.Add(nodeId);
                }
            }

            for (int i = unique.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < unique.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    if (IsDescendantOf(unique[i], unique[j], nodeById))
                    {
                        unique.RemoveAt(i);
                        break;
                    }
                }
            }

            unique.Sort((left, right) => GetSiblingIndex(nodeById, left).CompareTo(GetSiblingIndex(nodeById, right)));
            return unique.ToArray();
        }

        private static string ResolveGeneratedParentId(string[] memberRootIds, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (memberRootIds == null || memberRootIds.Length < 1 || nodeById == null)
            {
                return "root";
            }

            if (memberRootIds.Length == 1 && nodeById.TryGetValue(memberRootIds[0], out var singleNode) && singleNode != null)
            {
                return string.IsNullOrWhiteSpace(singleNode.parentId) ? "root" : singleNode.parentId;
            }

            var commonAncestorId = GetLowestCommonAncestor(memberRootIds, nodeById);
            return string.IsNullOrWhiteSpace(commonAncestorId) ? "root" : commonAncestorId;
        }

        private static string GetLowestCommonAncestor(string[] nodeIds, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (nodeIds == null || nodeIds.Length < 1 || nodeById == null)
            {
                return "root";
            }

            var firstAncestors = BuildAncestorChain(nodeIds[0], nodeById);
            for (int i = 0; i < firstAncestors.Count; i++)
            {
                string candidate = firstAncestors[i];
                bool common = true;
                for (int j = 1; j < nodeIds.Length; j++)
                {
                    if (!IsDescendantOrSelfOf(nodeIds[j], candidate, nodeById))
                    {
                        common = false;
                        break;
                    }
                }

                if (common)
                {
                    return candidate;
                }
            }

            return "root";
        }

        private static List<string> BuildAncestorChain(string nodeId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            var chain = new List<string>(8);
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                chain.Add("root");
                return chain;
            }

            string currentId = nodeId;
            for (int guard = 0; guard < 256; guard++)
            {
                if (string.IsNullOrWhiteSpace(currentId))
                {
                    chain.Add("root");
                    break;
                }

                chain.Add(currentId);
                if (!nodeById.TryGetValue(currentId, out var node) || node == null || string.IsNullOrWhiteSpace(node.parentId) || string.Equals(node.parentId, "root", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Add("root");
                    break;
                }

                currentId = node.parentId;
            }

            return chain;
        }

        private static bool IsUnderOwnerSubtreeAfterOwnerMove(OwnerRuntime owner, string nodeId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            if (owner.requiresGeneratedGroup)
            {
                return IsDescendantOfAny(owner.memberRootIds, nodeId, nodeById);
            }

            return IsDescendantOrSelfOf(nodeId, owner.runtimeNodeId, nodeById);
        }

        private static string ResolveExistingRoleMoveRoot(RoleRuntime role, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (role == null || string.IsNullOrWhiteSpace(role.existingCarrierNodeId))
            {
                return string.Empty;
            }

            return role.existingCarrierNodeId;
        }

        private static bool IsMemberRoot(string[] roots, string nodeId)
        {
            if (roots == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            for (int i = 0; i < roots.Length; i++)
            {
                if (string.Equals(roots[i], nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDescendantOfAny(string[] ancestorIds, string nodeId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (ancestorIds == null || ancestorIds.Length < 1 || string.IsNullOrWhiteSpace(nodeId) || nodeById == null)
            {
                return false;
            }

            for (int i = 0; i < ancestorIds.Length; i++)
            {
                string ancestorId = ancestorIds[i];
                if (string.IsNullOrWhiteSpace(ancestorId))
                {
                    continue;
                }

                if (IsDescendantOrSelfOf(nodeId, ancestorId, nodeById))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDescendantOf(string nodeId, string ancestorId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(ancestorId) || nodeById == null)
            {
                return false;
            }

            string currentId = nodeId;
            for (int guard = 0; guard < 256; guard++)
            {
                if (!nodeById.TryGetValue(currentId, out var node) || node == null || string.IsNullOrWhiteSpace(node.parentId))
                {
                    return false;
                }

                if (string.Equals(node.parentId, ancestorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(node.parentId, "root", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                currentId = node.parentId;
            }

            return false;
        }

        private static bool IsDescendantOrSelfOf(string nodeId, string ancestorId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            return string.Equals(nodeId, ancestorId, StringComparison.OrdinalIgnoreCase) || IsDescendantOf(nodeId, ancestorId, nodeById);
        }

        private static int GetHierarchyDistance(string nodeId, string ancestorId, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(ancestorId) || nodeById == null)
            {
                return -1;
            }

            if (string.Equals(nodeId, ancestorId, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            string currentId = nodeId;
            for (int depth = 1; depth < 256; depth++)
            {
                if (!nodeById.TryGetValue(currentId, out var node) || node == null || string.IsNullOrWhiteSpace(node.parentId))
                {
                    return -1;
                }

                if (string.Equals(node.parentId, ancestorId, StringComparison.OrdinalIgnoreCase))
                {
                    return depth;
                }

                if (string.Equals(node.parentId, "root", StringComparison.OrdinalIgnoreCase))
                {
                    return -1;
                }

                currentId = node.parentId;
            }

            return -1;
        }

        private static int GetMinimumSiblingIndex(string[] nodeIds, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (nodeIds == null || nodeIds.Length < 1)
            {
                return 0;
            }

            int minIndex = int.MaxValue;
            for (int i = 0; i < nodeIds.Length; i++)
            {
                int siblingIndex = GetSiblingIndex(nodeById, nodeIds[i]);
                if (siblingIndex < minIndex)
                {
                    minIndex = siblingIndex;
                }
            }

            return minIndex == int.MaxValue ? 0 : minIndex;
        }

        private static int GetSiblingIndex(Dictionary<string, AiAnalysisNodeEntry> nodeById, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)
                || nodeById == null
                || !nodeById.TryGetValue(nodeId, out var node)
                || node == null)
            {
                return int.MaxValue;
            }

            return node.siblingIndex;
        }

        private static string ResolveCurrentAuditType(string targetId, Dictionary<string, NodeState> nodeStates)
        {
            if (!string.IsNullOrWhiteSpace(targetId)
                && nodeStates != null
                && nodeStates.TryGetValue(targetId, out var state)
                && state != null)
            {
                return state.currentType.ToString();
            }

            return GUIType.Null.ToString();
        }

        private static GUIType NormalizeCurrentType(AiAnalysisNodeEntry node)
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

        private static float NormalizeConfidence(float value)
        {
            return value > 0f && value <= 1f ? value : 0.8f;
        }

        private static string BuildGeneratedNodeId(string prefix, GUIType uiType, string semanticId)
        {
            return string.Concat(prefix, uiType, ":", SanitizeGeneratedToken(semanticId));
        }

        private static string BuildGeneratedNodeName(string prefix, GUIType uiType, string semanticId)
        {
            if (string.Equals(prefix, "gen_role", StringComparison.Ordinal))
            {
                return uiType.ToString();
            }

            if (string.Equals(prefix, "gen_owner", StringComparison.Ordinal))
            {
                return string.Concat(uiType, "_", BuildStableShortSuffix(semanticId));
            }

            string shortId = SanitizeGeneratedToken(semanticId);
            if (shortId.Length > 16)
            {
                shortId = shortId.Substring(0, 16);
            }

            return string.Concat(uiType, "_", shortId);
        }

        private static string BuildGeneratedRoleNodeName(GUIType roleType, OwnerRuntime owner, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (roleType == GUIType.Background)
            {
                string parentName = ResolveGeneratedRoleParentName(owner, nodeById);
                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    return string.Concat(parentName, "_bg");
                }
            }

            return roleType.ToString();
        }

        private static string ResolveGeneratedRoleParentName(OwnerRuntime owner, Dictionary<string, AiAnalysisNodeEntry> nodeById)
        {
            if (owner == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(owner.existingCarrierNodeId)
                && nodeById != null
                && nodeById.TryGetValue(owner.existingCarrierNodeId, out var ownerNode)
                && ownerNode != null)
            {
                string sourceName = !string.IsNullOrWhiteSpace(ownerNode.name) ? ownerNode.name : ownerNode.layerName;
                string sanitized = SanitizeGeneratedToken(sourceName);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    return sanitized;
                }
            }

            if (!string.IsNullOrWhiteSpace(owner.generatedNodeName))
            {
                string sanitized = SanitizeGeneratedToken(owner.generatedNodeName);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    return sanitized;
                }
            }

            return string.Empty;
        }

        private static string BuildStableShortSuffix(string semanticId)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string value = string.IsNullOrWhiteSpace(semanticId) ? "auto" : semanticId.Trim();
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return (hash & 0xFFFFu).ToString("X4");
            }
        }

        private static string SanitizeGeneratedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "auto";
            }

            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
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
