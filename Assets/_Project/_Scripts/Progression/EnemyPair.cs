using System;
using SliceIt.Enemies;
using UnityEngine;

namespace SliceIt.Progression
{
    public enum PairResolutionState
    {
        BothActive,
        WindowRunning,
        Merged,
        Restored
    }

    [Serializable]
    public class EnemyPair
    {
        public int PairId;
        public EnemyEntity EnemyA;
        public EnemyEntity EnemyB;
        public Transform MergeSpawnPoint;
        public GameObject NextRankPrefab;
        public Color PairColor = Color.cyan;

        [NonSerialized] public PairResolutionState ResolutionState = PairResolutionState.BothActive;
        [NonSerialized] public float WindowTimer = 0f;
        [NonSerialized] public EnemyEntity FirstDefeated = null;

        public bool IsWindowActive => ResolutionState == PairResolutionState.WindowRunning;

        public void Initialize()
        {
            ResolutionState = PairResolutionState.BothActive;
            WindowTimer = 0f;
            FirstDefeated = null;

            if (EnemyA != null) EnemyA.InitializePairInfo(EnemyA.EnemyId, PairId, EnemyA.Rank, PairColor);
            if (EnemyB != null) EnemyB.InitializePairInfo(EnemyB.EnemyId, PairId, EnemyB.Rank, PairColor);
        }
    }
}
