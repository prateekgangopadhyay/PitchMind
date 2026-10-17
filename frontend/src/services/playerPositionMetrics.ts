export interface ObservedPositionMetric {
  label: string;
  value: string;
}

export interface PlayerPositionLike {
  speed?: number;
  distanceCovered?: number;
  heartRate?: number;
  passAccuracy?: number;
  heatIndex?: number;
  x?: number;
  y?: number;
}

/** Returns only values present and valid in the supplied position snapshot. */
export function getObservedPositionMetrics(player: PlayerPositionLike | null | undefined): ObservedPositionMetric[] {
  if (!player || typeof player !== 'object') return [];
  const metrics: ObservedPositionMetric[] = [];
  if (Number.isFinite(player.speed) && (player.speed as number) > 0) {
    metrics.push({ label: 'Instant speed', value: `${(player.speed as number).toFixed(1)} km/h` });
  }
  if (Number.isFinite(player.distanceCovered) && (player.distanceCovered as number) > 0) {
    metrics.push({ label: 'Distance covered', value: `${(player.distanceCovered as number).toFixed(2)} km` });
  }
  if (Number.isFinite(player.heartRate) && (player.heartRate as number) > 0) {
    metrics.push({ label: 'Heart rate', value: `${player.heartRate} bpm` });
  }
  if (Number.isFinite(player.passAccuracy) && (player.passAccuracy as number) > 0 && (player.passAccuracy as number) <= 100) {
    metrics.push({ label: 'Pass accuracy', value: `${player.passAccuracy}%` });
  }
  if (Number.isFinite(player.heatIndex) && (player.heatIndex as number) > 0 && (player.heatIndex as number) <= 1) {
    metrics.push({ label: 'Supplied activity index', value: `${((player.heatIndex as number) * 100).toFixed(0)}%` });
  }
  if (Number.isFinite(player.x) && Number.isFinite(player.y)) {
    metrics.push({ label: 'Pitch position', value: `(${(player.x as number).toFixed(1)}, ${(player.y as number).toFixed(1)})` });
  }
  return metrics;
}

