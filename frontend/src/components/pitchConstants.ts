export type RegionMode = '18zones' | 'channels' | 'thirds' | 'off';
export type TurfTone = 'daylight' | 'sunlit' | 'emerald' | 'dark';

export interface ZoneDefinition {
  id: number;
  name: string;
  tag: string;
  x: number;
  y: number;
  w: number;
  h: number;
  color: string;
  stroke: string;
  desc: string;
}

export interface TurfPalette {
  stripeA: string;
  stripeB: string;
  apron: string;
  border: string;
}

export const TURF_PALETTES: Record<TurfTone, TurfPalette> = {
  daylight: {
    stripeA: '#4db86e',
    stripeB: '#44a962',
    apron: '#3a9c56',
    border: '#2f8648',
  },
  sunlit: {
    stripeA: '#58c97b',
    stripeB: '#4ebd70',
    apron: '#43b063',
    border: '#389c54',
  },
  emerald: {
    stripeA: '#38a169',
    stripeB: '#2f855a',
    apron: '#276749',
    border: '#22543d',
  },
  dark: {
    stripeA: '#166534',
    stripeB: '#14532d',
    apron: '#0f381e',
    border: '#092413',
  },
};

export const ZONE_DEFINITIONS: ZoneDefinition[] = [
  { id: 1, name: 'Defensive Left Wing', tag: '1 · LW (Def)', x: 0, y: 0, w: 33.33, h: 18, color: 'rgba(148,163,184,0.12)', stroke: 'rgba(148,163,184,0.4)', desc: 'Defensive flank containment and clearance zone' },
  { id: 2, name: 'Defensive Left Half-Space', tag: '2 · LHS (Def)', x: 0, y: 18, w: 33.33, h: 18.5, color: 'rgba(6,182,212,0.12)', stroke: 'rgba(6,182,212,0.45)', desc: 'Left defensive half-space for build-up and recovery actions' },
  { id: 3, name: 'Central Defensive Box', tag: '3 · DEF BOX', x: 0, y: 36.5, w: 33.33, h: 27, color: 'rgba(99,102,241,0.14)', stroke: 'rgba(99,102,241,0.5)', desc: 'Central defensive area in front of the penalty box' },
  { id: 4, name: 'Defensive Right Half-Space', tag: '4 · RHS (Def)', x: 0, y: 63.5, w: 33.33, h: 18.5, color: 'rgba(6,182,212,0.12)', stroke: 'rgba(6,182,212,0.45)', desc: 'Right defensive half-space for build-up and recovery actions' },
  { id: 5, name: 'Defensive Right Wing', tag: '5 · RW (Def)', x: 0, y: 82, w: 33.33, h: 18, color: 'rgba(148,163,184,0.12)', stroke: 'rgba(148,163,184,0.4)', desc: 'Right defensive flank containment' },

  { id: 6, name: 'Left Midfield Flank', tag: '6 · LW (Mid)', x: 33.33, y: 0, w: 33.34, h: 18, color: 'rgba(148,163,184,0.10)', stroke: 'rgba(148,163,184,0.4)', desc: 'Wide midfield progression and touchline combination play' },
  { id: 7, name: 'Left Midfield Half-Space', tag: '7 · LHS (Mid)', x: 33.33, y: 18, w: 33.34, h: 18.5, color: 'rgba(6,182,212,0.14)', stroke: 'rgba(6,182,212,0.5)', desc: 'Left midfield corridor between the centre and touchline' },
  { id: 8, name: 'Central Midfield Engine Room', tag: '8 · ENGINE ROOM', x: 33.33, y: 36.5, w: 33.34, h: 27, color: 'rgba(16,185,129,0.15)', stroke: 'rgba(16,185,129,0.55)', desc: 'Central pitch control; tempo setting and defensive transition prevention' },
  { id: 9, name: 'Right Midfield Half-Space', tag: '9 · RHS (Mid)', x: 33.33, y: 63.5, w: 33.34, h: 18.5, color: 'rgba(6,182,212,0.14)', stroke: 'rgba(6,182,212,0.5)', desc: 'Right midfield corridor between the centre and touchline' },
  { id: 10, name: 'Right Midfield Flank', tag: '10 · RW (Mid)', x: 33.33, y: 82, w: 33.34, h: 18, color: 'rgba(148,163,184,0.10)', stroke: 'rgba(148,163,184,0.4)', desc: 'Right touchline isolation and overload support' },

  { id: 11, name: 'Left Attacking Wing', tag: '11 · ATT WING (L)', x: 66.67, y: 0, w: 33.33, h: 18, color: 'rgba(148,163,184,0.12)', stroke: 'rgba(148,163,184,0.45)', desc: '1v1 wide wing isolation and cross delivery sector' },
  { id: 12, name: 'Left Attacking Half-Space', tag: '12 · CUTBACK (L)', x: 66.67, y: 18, w: 16.83, h: 18.5, color: 'rgba(6,182,212,0.16)', stroke: 'rgba(6,182,212,0.6)', desc: 'Left attacking corridor between the centre and touchline' },
  { id: 13, name: 'Left Deep Box Pocket', tag: '13 · BOX (L)', x: 83.5, y: 18, w: 16.5, h: 18.5, color: 'rgba(239,68,68,0.14)', stroke: 'rgba(239,68,68,0.55)', desc: 'Left side of the penalty area' },
  { id: 14, name: 'Zone 14', tag: '14 · ZONE 14', x: 66.67, y: 36.5, w: 16.83, h: 27, color: 'rgba(245,158,11,0.22)', stroke: 'rgba(251,191,36,0.85)', desc: 'Central area just outside the penalty box; displayed as a spatial reference zone' },
  { id: 15, name: 'Central Attacking Box', tag: '15 · ATTACKING BOX', x: 83.5, y: 36.5, w: 16.5, h: 27, color: 'rgba(239,68,68,0.22)', stroke: 'rgba(248,113,113,0.85)', desc: 'Central area inside the penalty box' },
  { id: 16, name: 'Right Deep Box Pocket', tag: '16 · BOX (R)', x: 83.5, y: 63.5, w: 16.5, h: 18.5, color: 'rgba(239,68,68,0.14)', stroke: 'rgba(239,68,68,0.55)', desc: 'Right side of the penalty area' },
  { id: 17, name: 'Right Attacking Half-Space', tag: '17 · RHS POCKET', x: 66.67, y: 63.5, w: 16.83, h: 18.5, color: 'rgba(6,182,212,0.16)', stroke: 'rgba(6,182,212,0.6)', desc: 'Right attacking corridor between the centre and touchline' },
  { id: 18, name: 'Right Attacking Wing', tag: '18 · ATT WING (R)', x: 66.67, y: 82, w: 33.33, h: 18, color: 'rgba(148,163,184,0.12)', stroke: 'rgba(148,163,184,0.45)', desc: 'Wide right crossing zone and corner kick generation area' },
];
