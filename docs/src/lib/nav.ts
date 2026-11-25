import { getCollection, type CollectionEntry } from 'astro:content';

export type Doc = CollectionEntry<'docs'>;
export type NavItem = { id: string; title: string; short: string; href: string };
export type NavGroup = { label: string; items: NavItem[] };

const base = import.meta.env.BASE_URL.replace(/\/$/, '');

export const href = (id: string) => `${base}/docs/${id === 'index' ? '' : id + '/'}`;

/** The document's own title: its first "# " heading. */
export function titleOf(doc: Doc): string {
  const heading = doc.body?.match(/^#\s+(.+)$/m)?.[1] ?? doc.id;
  return heading.replace(/`/g, '').trim();
}

/** The title without its number or "ADR nnnn —" prefix, for the sidebar. */
function shortTitle(title: string): string {
  return title.replace(/^ADR \d+\s+—\s+/, '').replace(/^\d+\s+—\s+/, '');
}

// The numbered documents by what a reader looks for; anything not listed falls into "Reference".
const groups: { label: string; ids: string[] }[] = [
  { label: 'Start here', ids: ['index', '00-vision', '20-status', '06-roadmap', 'contributing'] },
  { label: 'How it works', ids: ['02-architecture', '03-lowering', '04-runtime', '12-corelib', '21-ownership', '01-prior-art'] },
  { label: 'Features', ids: ['08-drop-in', '10-async', '14-threading', '11-bcl-value-types', '16-interop-io', '19-globalization', '13-debugging', '17-security'] },
  { label: 'Testing and quality', ids: ['05-testing', '09-conformance', '15-conformance2', '18-compatibility', '07-spike-log'] },
];

export async function navigation(): Promise<NavGroup[]> {
  const docs = await getCollection('docs');
  const byId = new Map(docs.map((d) => [d.id, d]));
  const item = (d: Doc): NavItem => ({ id: d.id, title: titleOf(d), short: shortTitle(titleOf(d)), href: href(d.id) });
  const used = new Set<string>();
  const result: NavGroup[] = groups.map((g) => ({
    label: g.label,
    items: g.ids.filter((id) => byId.has(id)).map((id) => { used.add(id); return item(byId.get(id)!); }),
  }));
  const rest = (prefix: string) => docs.filter((d) => d.id.startsWith(prefix) && !used.has(d.id)).sort((a, b) => a.id.localeCompare(b.id));
  const reference = docs.filter((d) => !d.id.includes('/') && !used.has(d.id)).sort((a, b) => a.id.localeCompare(b.id));
  if (reference.length) result.push({ label: 'Reference', items: reference.map(item) });
  result.push({ label: 'Decisions (ADRs)', items: rest('adr/').map((d) => ({ ...item(d), short: `${d.id.slice(4, 8)} · ${shortTitle(titleOf(d))}` })) });
  result.push({ label: 'Framework IL notes', items: rest('il-first/').map(item) });
  return result;
}

/** The documents in reading order (the sidebar's), for previous/next links. */
export async function readingOrder(): Promise<NavItem[]> {
  return (await navigation()).flatMap((g) => g.items);
}
