/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 */

#if UNITY_EDITOR
using System;
using cn.efunstudio.psdreader.PsdParser;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// Converts the transform metadata stored in a PSD layer into Unity UI Euler angles.
    /// Photoshop uses a y-down coordinate system, while Unity UI uses y-up coordinates.
    /// </summary>
    internal static class PsdLayerTransformUtility
    {
        private const double MatrixEpsilon = 1e-8;
        private const double OrthogonalityTolerance = 1e-4;

        internal static bool TryGetTextUnityRotation(PsdLayer layer, out Vector3 rotation)
        {
            rotation = Vector3.zero;
            if (layer == null || !layer.TryGetTextTransform(out var transforms))
            {
                return false;
            }

            // TySh.Transforms is [xx, xy, yx, yy, tx, ty]. The first column
            // describes the transformed text x-axis in Photoshop coordinates.
            double xx = transforms[0];
            double xy = transforms[1];
            double yx = transforms[2];
            double yy = transforms[3];
            if (!IsFinite(xx) || !IsFinite(xy) || !IsFinite(yx) || !IsFinite(yy))
            {
                return false;
            }

            double xScale = Math.Sqrt(xx * xx + xy * xy);
            double yScale = Math.Sqrt(yx * yx + yy * yy);
            if (!IsFinite(xScale) || !IsFinite(yScale)
                || xScale <= MatrixEpsilon || yScale <= MatrixEpsilon)
            {
                return false;
            }

            // A shear or a reflected matrix is not a pure rotation component.
            // Uniform and non-uniform scale are deliberately ignored here: the
            // existing text-size path owns font scaling, while this method only
            // synchronizes the rotation component.
            double dot = xx * yx + xy * yy;
            double determinant = xx * yy - xy * yx;
            double scaleProduct = xScale * yScale;
            if (!IsFinite(dot) || !IsFinite(determinant) || !IsFinite(scaleProduct)
                || Math.Abs(dot) > OrthogonalityTolerance * scaleProduct
                || determinant <= MatrixEpsilon * scaleProduct)
            {
                return false;
            }

            // Photoshop's positive screen-space angle is clockwise because its
            // y-axis points down. Negating it produces Unity's positive Z angle.
            float photoshopAngle = (float)(Math.Atan2(xy, xx) * Mathf.Rad2Deg);
            rotation.z = Mathf.DeltaAngle(0f, -photoshopAngle);
            return true;
        }

        internal static bool TryGetPlacedUnityRotation(PsdLayer layer, out Vector3 rotation)
        {
            rotation = Vector3.zero;
            if (layer == null || !layer.TryGetPlacementTransform(out var transforms))
            {
                return false;
            }

            double dx = transforms[2] - transforms[0];
            double dy = transforms[3] - transforms[1];
            if (!IsFinite(dx) || !IsFinite(dy))
            {
                return false;
            }

            double edgeLength = Math.Sqrt(dx * dx + dy * dy);
            if (!IsFinite(edgeLength))
            {
                return false;
            }
            if (edgeLength <= MatrixEpsilon)
            {
                dx = transforms[4] - transforms[6];
                dy = transforms[5] - transforms[7];
                edgeLength = Math.Sqrt(dx * dx + dy * dy);
            }
            if (!IsFinite(dx) || !IsFinite(dy)
                || !IsFinite(edgeLength) || edgeLength <= MatrixEpsilon)
            {
                return false;
            }

            float photoshopAngle = (float)(Math.Atan2(dy, dx) * Mathf.Rad2Deg);
            rotation.z = Mathf.DeltaAngle(0f, -photoshopAngle);
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
#endif
