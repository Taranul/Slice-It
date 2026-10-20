using System;
using System.Collections.Generic;
using UnityEngine;

namespace SliceIt.Combat
{
    public struct TrajectoryPoint2D
    {
        public Vector2 ScreenPosition;
        public float Timestamp;

        public TrajectoryPoint2D(Vector2 screenPosition, float timestamp)
        {
            ScreenPosition = screenPosition;
            Timestamp = timestamp;
        }
    }

    public struct TrajectorySegment3D
    {
        public Vector3 StartPoint;
        public Vector3 EndPoint;
        public Vector3 BladeTip;
        public Vector3 BladeBase;

        public TrajectorySegment3D(Vector3 startPoint, Vector3 endPoint, Vector3 bladeTip, Vector3 bladeBase)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            BladeTip = bladeTip;
            BladeBase = bladeBase;
        }
    }

    /// <summary>
    /// Decoupled interface to convert 2D mouse input into a 3D cutting trajectory.
    /// Allows swapping the mathematical projection model without changing combat or network logic.
    /// </summary>
    public interface ITrajectoryConverter3D
    {
        List<TrajectorySegment3D> ConvertTo3D(
            IReadOnlyList<TrajectoryPoint2D> inputPoints,
            Camera camera,
            float reachDistance,
            float nearOffset
        );
    }
}
