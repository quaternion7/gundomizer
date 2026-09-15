namespace Gundomizer
{
    internal static class SpawnSafetyPolicy
    {
        internal static string ReflexSightProblem(bool hasUiSpawnPoint, bool hasComponents, bool hasNullComponent)
        {
            if (!hasUiSpawnPoint) return "ReflexSightController.UISpawnPoint is missing; native Awake would fail";
            if (!hasComponents || hasNullComponent)
                return "ReflexSightController.Components is incomplete; native Awake would fail";
            return null;
        }
    }
}
