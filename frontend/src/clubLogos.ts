export const CLUB_LOGOS: Record<string, string> = {
  'arsenal': '/crests/arsenal.svg',
  'aston villa': '/crests/aston-villa.svg',
  'bournemouth': '/crests/bournemouth.svg',
  'brentford': '/crests/brentford.svg',
  'brighton': '/crests/brighton.svg',
  'chelsea': '/crests/chelsea.svg',
  'crystal palace': '/crests/crystal-palace.svg',
  'everton': '/crests/everton.svg',
  'fulham': '/crests/fulham.svg',
  'ipswich': '/crests/ipswich.svg',
  'ipswich town': '/crests/ipswich.svg',
  'leicester': '/crests/leicester.svg',
  'leicester city': '/crests/leicester.svg',
  'liverpool': '/crests/liverpool.svg',
  'man city': '/crests/manchester-city.svg',
  'manchester city': '/crests/manchester-city.svg',
  'man utd': '/crests/manchester-united.svg',
  'manchester united': '/crests/manchester-united.svg',
  'newcastle': '/crests/newcastle.svg',
  'newcastle united': '/crests/newcastle.svg',
  "nott'm forest": '/crests/nottingham-forest.svg',
  'nottingham forest': '/crests/nottingham-forest.svg',
  'southampton': '/crests/southampton.svg',
  'tottenham': '/crests/tottenham.svg',
  'tottenham hotspur': '/crests/tottenham.svg',
  'spurs': '/crests/tottenham.svg',
  'west ham': '/crests/west-ham.svg',
  'west ham united': '/crests/west-ham.svg',
  'wolves': '/crests/wolves.svg',
  'wolverhampton wanderers': '/crests/wolves.svg'
};

export function resolveClubLogo(name?: string, logoUrl?: string): string {
  if (logoUrl) return logoUrl;
  if (!name) return '';
  const lower = name.trim().toLowerCase();
  if (CLUB_LOGOS[lower]) return CLUB_LOGOS[lower];
  for (const [key, url] of Object.entries(CLUB_LOGOS)) {
    if (lower.includes(key) || key.includes(lower)) {
      return url;
    }
  }
  return '';
}
