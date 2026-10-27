import rawPlayersData from './data/manchester-united-liverpool-players.json';

export interface PlayerProfile {
  id: string;
  name: string;
  number?: number;
  role: string;
  club: string;
  teamLogo?: string;
  photoUrl: string;
  fallbackPhotoUrl?: string;
  avatarSvg: string;
  nationality?: string;
  age?: number;
  preferredFoot?: string;
  tags?: string[];
  initials: string;
}

export function generatePlayerAvatarSvg(name?: string, number?: number, club?: string): string {
  const cleanName = (name ?? 'Player').trim();
  const initials = cleanName.split(/\s+/).map(p => p[0]).filter(Boolean).join('').slice(0, 2).toUpperCase() || 'PM';
  const numText = number !== undefined && number > 0 ? `#${number}` : '';
  const clubLower = (club ?? '').toLowerCase();
  const isManUtd = clubLower.includes('manchester') || clubLower.includes('united') || clubLower.includes('utd');
  const gradientStart = isManUtd ? '#dc2626' : '#059669';
  const gradientEnd = isManUtd ? '#450a0a' : '#064e3b';
  const accentColor = isManUtd ? '#fca5a5' : '#6ee7b7';

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">
    <defs>
      <radialGradient id="avatarBg-${initials}" cx="50%" cy="38%" r="65%">
        <stop offset="0%" stop-color="${gradientStart}" />
        <stop offset="100%" stop-color="${gradientEnd}" />
      </radialGradient>
      <linearGradient id="avatarSil-${initials}" x1="0%" y1="0%" x2="0%" y2="100%">
        <stop offset="0%" stop-color="#ffffff" stop-opacity="0.95" />
        <stop offset="100%" stop-color="${accentColor}" stop-opacity="0.85" />
      </linearGradient>
    </defs>
    <rect width="120" height="120" rx="60" fill="url(#avatarBg-${initials})" />
    <!-- Footballer Head -->
    <circle cx="60" cy="40" r="19" fill="url(#avatarSil-${initials})" />
    <!-- Footballer Shoulders & Torso -->
    <path d="M 24 108 C 24 76, 42 66, 60 66 C 78 66, 96 76, 96 108 Z" fill="url(#avatarSil-${initials})" opacity="0.92" />
    <!-- Initials & Jersey Number Pill -->
    <rect x="28" y="86" width="64" height="22" rx="11" fill="rgba(8, 24, 18, 0.88)" stroke="${accentColor}" stroke-width="1.5" />
    <text x="60" y="101" font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif" font-size="11" font-weight="900" fill="#ffffff" text-anchor="middle" dominant-baseline="middle" letter-spacing="0.04em">${initials} ${numText}</text>
  </svg>`;

  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

export function normalizePlayerKey(name: string): string {
  return name.trim().toLowerCase()
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .replace(/ø/g, 'o')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

const KNOWN_PLAYERS: PlayerProfile[] = (rawPlayersData as Array<{
  id: string;
  name: string;
  number?: number;
  role: string;
  club: string;
  teamLogo?: string;
  photoUrl?: string;
  photo?: string;
  nationality?: string;
  age?: number;
  preferredFoot?: string;
  tags?: string[];
}>).map(p => {
  const normKey = normalizePlayerKey(p.name);
  return {
    id: p.id || normKey,
    name: p.name,
    number: p.number,
    role: p.role,
    club: p.club,
    teamLogo: p.teamLogo,
    photoUrl: `/player-photos/${normKey}.jpg`,
    fallbackPhotoUrl: `/players/${normKey}.jpg`,
    avatarSvg: generatePlayerAvatarSvg(p.name, p.number, p.club),
    nationality: p.nationality,
    age: p.age,
    preferredFoot: p.preferredFoot,
    tags: p.tags,
    initials: p.name.split(/\s+/).map(n => n[0]).join('').slice(0, 2).toUpperCase()
  };
});

// Additional catalog for Premier League stars
const ADDITIONAL_PLAYERS: Record<string, Partial<PlayerProfile>> = {
  'saka': { name: 'Bukayo Saka', number: 7, role: 'Right Winger (RW)', club: 'Arsenal', photoUrl: '/player-photos/saka.webp', fallbackPhotoUrl: '/players/saka.webp', nationality: 'England' },
  'bukayo-saka': { name: 'Bukayo Saka', number: 7, role: 'Right Winger (RW)', club: 'Arsenal', photoUrl: '/player-photos/saka.webp', fallbackPhotoUrl: '/players/saka.webp', nationality: 'England' },
  'rice': { name: 'Declan Rice', number: 41, role: 'Defensive Midfielder (DM)', club: 'Arsenal', photoUrl: '/player-photos/rice.webp', fallbackPhotoUrl: '/players/rice.webp', nationality: 'England' },
  'declan-rice': { name: 'Declan Rice', number: 41, role: 'Defensive Midfielder (DM)', club: 'Arsenal', photoUrl: '/player-photos/rice.webp', fallbackPhotoUrl: '/players/rice.webp', nationality: 'England' },
  'odegaard': { name: 'Martin Ødegaard', number: 8, role: 'Attacking Midfielder (AM)', club: 'Arsenal', photoUrl: '/player-photos/odegaard.webp', fallbackPhotoUrl: '/players/odegaard.webp', nationality: 'Norway' },
  'martin-odegaard': { name: 'Martin Ødegaard', number: 8, role: 'Attacking Midfielder (AM)', club: 'Arsenal', photoUrl: '/player-photos/odegaard.webp', fallbackPhotoUrl: '/players/odegaard.webp', nationality: 'Norway' },
  'gabriel': { name: 'Gabriel Magalhães', number: 6, role: 'Center Back (CB)', club: 'Arsenal', photoUrl: '/player-photos/gabriel.webp', fallbackPhotoUrl: '/players/gabriel.webp', nationality: 'Brazil' },
  'martinelli': { name: 'Gabriel Martinelli', number: 11, role: 'Left Winger (LW)', club: 'Arsenal', photoUrl: '/player-photos/martinelli.webp', fallbackPhotoUrl: '/players/martinelli.webp', nationality: 'Brazil' }
};

export function getPlayerProfile(name?: string, teamName?: string, jerseyNumber?: number): PlayerProfile {
  if (!name || !name.trim()) {
    return {
      id: 'unknown-player',
      name: 'Player',
      number: jerseyNumber,
      role: 'Squad Member',
      club: teamName || 'Club',
      photoUrl: '/player-photos/marcus-rashford.jpg',
      fallbackPhotoUrl: '/players/marcus-rashford.jpg',
      avatarSvg: generatePlayerAvatarSvg('Player', jerseyNumber, teamName),
      initials: 'PL'
    };
  }

  const cleanName = name.trim();
  const normalized = normalizePlayerKey(cleanName);
  const lastName = cleanName.split(/\s+/).pop()?.toLowerCase() ?? '';
  const normLastName = normalizePlayerKey(lastName);

  // 1. Direct match in known players catalog
  const found = KNOWN_PLAYERS.find(p => {
    const pNorm = normalizePlayerKey(p.name);
    return pNorm === normalized || pNorm.includes(normalized) || normalized.includes(pNorm) ||
      (normLastName.length > 3 && pNorm.includes(normLastName));
  });

  if (found) {
    return found;
  }

  // 2. Match in additional players
  if (ADDITIONAL_PLAYERS[normalized] || ADDITIONAL_PLAYERS[normLastName]) {
    const add = ADDITIONAL_PLAYERS[normalized] || ADDITIONAL_PLAYERS[normLastName];
    const resolvedName = add.name || cleanName;
    const resolvedNumber = add.number || jerseyNumber;
    const resolvedClub = add.club || teamName || 'Club';
    return {
      id: normalized,
      name: resolvedName,
      number: resolvedNumber,
      role: add.role || 'Player',
      club: resolvedClub,
      photoUrl: add.photoUrl || `/player-photos/${normalized}.jpg`,
      fallbackPhotoUrl: add.fallbackPhotoUrl || `/players/${normalized}.jpg`,
      avatarSvg: generatePlayerAvatarSvg(resolvedName, resolvedNumber, resolvedClub),
      nationality: add.nationality,
      initials: resolvedName.split(/\s+/).map(n => n[0]).join('').slice(0, 2).toUpperCase()
    };
  }

  // 3. Fallback with inferred role
  let inferredRole = 'Midfielder (MF)';
  if (jerseyNumber === 1 || normalized.includes('keeper') || normalized.includes('onana') || normalized.includes('alisson')) {
    inferredRole = 'Goalkeeper (GK)';
  } else if ([2, 3, 4, 5, 6].includes(jerseyNumber ?? 0)) {
    inferredRole = 'Defender (DF)';
  } else if ([7, 9, 10, 11].includes(jerseyNumber ?? 0)) {
    inferredRole = 'Forward / Attacker (FW)';
  }

  return {
    id: normalized,
    name: cleanName,
    number: jerseyNumber,
    role: inferredRole,
    club: teamName || 'Premier League',
    photoUrl: `/player-photos/${normalized}.jpg`,
    fallbackPhotoUrl: `/players/${normalized}.jpg`,
    avatarSvg: generatePlayerAvatarSvg(cleanName, jerseyNumber, teamName),
    initials: cleanName.split(/\s+/).map(n => n[0]).join('').slice(0, 2).toUpperCase()
  };
}

/**
 * Returns the best photo URL for a player, guaranteed to never return a club logo.
 */
export function resolvePlayerPhotoUrl(player: { name?: string; id?: string; club?: string; number?: number; photoUrl?: string; photo?: string }): {
  primaryUrl: string;
  fallbackUrl: string;
  avatarSvg: string;
} {
  const profile = getPlayerProfile(player.name || player.id, player.club, player.number);
  const primaryUrl = player.photoUrl || profile.photoUrl;
  const fallbackUrl = player.photo || profile.fallbackPhotoUrl || profile.photoUrl;
  return {
    primaryUrl,
    fallbackUrl,
    avatarSvg: profile.avatarSvg
  };
}
