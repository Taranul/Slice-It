using System.Collections.Generic;
using UnityEngine;

namespace SliceIt.Combat
{
    /// <summary>
    /// Default trajectory converter: projects the 2D mouse gesture as a cutting ribbon
    /// extending from the camera forward (near reach to full blade reach).
    /// </summary>
    public class CameraRibbonTrajectoryConverter : ITrajectoryConverter3D
    {
        public List<TrajectorySegment3D> ConvertTo3D(
            IReadOnlyList<TrajectoryPoint2D> inputPoints,
            Camera camera,
            float reachDistance,
            float nearOffset)
        {
            var segments = new List<TrajectorySegment3D>();
            if (inputPoints == null || inputPoints.Count < 2 || camera == null)
            {
                return segments;
            }

            Vector3 prevBase = Vector3.zero;
            Vector3 prevTip = Vector3.zero;
            Vector3 prevCenter = Vector3.zero;

            for (int i = 0; i < inputPoints.Count; i++)
            {
                Ray ray = camera.ScreenPointToRay(inputPoints[i].ScreenPosition);
                Vector3 currentBase = ray.origin + ray.direction * nearOffset;
                Vector3 currentTip = ray.origin + ray.direction * reachDistance;
                Vector3 currentCenter = (currentBase + currentTip) * 0.5f;

                if (i > 0)
                {
                    segments.Add(new TrajectorySegment3D(prevCenter, currentCenter, currentTip, currentBase));
                }

                prevBase = currentBase;
                prevTip = currentTip;
                prevCenter = currentCenter;
            }

            return segments;
        }
    }
}
