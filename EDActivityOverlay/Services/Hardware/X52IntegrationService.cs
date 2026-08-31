using System.Collections.Generic;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;

namespace EDActivityOverlay.Services.Hardware
{
    internal static class X52MiningCopilotFormatter
    {
        // Возвращает 3 строки для MFD. Простая, безопасная реализация; можно расширить позже.
        public static string[] BuildLines(
            MiningSessionSnapshot session,
            MiningCollectorActivitySnapshot collectors,
            string targetCommodity,
            double minProportion)
        {
            string l0 = $"MINING {session.ProspectorsLaunched}/{session.CollectorsLaunched}";
            string l1 = $"CRACKED {session.CrackedAsteroids} CARGO {session.CargoUsed}/{session.CargoCapacity}";
            string l2 = $"COLL {collectors.EstimatedActive}/{collectors.Capacity}";

            // Укоротить строки до допустимой длины
            int max = X52DisplayFormatter.MaximumLineLength;
            return new[]
            {
                (l0.Length <= max) ? l0 : l0.Substring(0, max),
                (l1.Length <= max) ? l1 : l1.Substring(0, max),
                (l2.Length <= max) ? l2 : l2.Substring(0, max)
            };
        }

        // Возвращает состояние светодиодов. Пока делегируем к существующему форматтеру отображения.
        public static IReadOnlyDictionary<int, bool> BuildLedComponents(
            GameStateSnapshot state,
            MiningSessionSnapshot session,
            MiningCollectorActivitySnapshot collectors,
            string targetCommodity,
            double minProportion,
            long animationStep)
        {
            // Используем базовый форматтер для режима Mining; можно расширить логику, используя session/collectors.
            return X52DisplayFormatter.BuildLedComponents(state, ActivityType.Mining, animationStep);
        }
    }
}
