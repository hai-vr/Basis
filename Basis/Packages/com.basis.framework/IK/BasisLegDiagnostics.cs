namespace Basis.IK
{
    public struct BasisLegDiagnostics
    {
        public float ReachRatio;
        public float KneeAngleDeg;
        public float AxisSource;
        public float HintApplied;
        public float ModelHintUsed;
        public float ModelConfidence;
        public float HintDistrust;
        public float RawSwivelDeg;
        public float SmoothSwivelDeg;
        public float Conditioning;
        public float HoldGate;
        public float AnteriorGuardApplied;
        public float Seeded;

        public static string Header =>
            "leg,reach,kneeDeg,axisSrc,hintApplied,modelUsed,modelConf,distrust,rawSwivel,smoothSwivel,cond,holdGate,antGuard,seeded";

        public string ToRow(string leg) =>
            $"{leg},{ReachRatio:F4},{KneeAngleDeg:F2},{AxisSource:F0},{HintApplied:F0},{ModelHintUsed:F0}," +
            $"{ModelConfidence:F3},{HintDistrust:F3},{RawSwivelDeg:F2},{SmoothSwivelDeg:F2}," +
            $"{Conditioning:F4},{HoldGate:F3},{AnteriorGuardApplied:F0},{Seeded:F0}";
    }
}
