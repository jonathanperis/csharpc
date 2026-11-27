import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';

// The documents stay where they are (docs/*.md, docs/adr, docs/il-first) and keep reading well on GitHub; the site renders the same files.
const docs = defineCollection({
  loader: glob({
    base: '.',
    pattern: ['*.md', 'adr/*.md', 'il-first/*.md'],
    // "05-testing.md" → "05-testing", "adr/0014-compile-time.md" → "adr/0014-compile-time", the README is the docs index.
    generateId: ({ entry }) => {
      const id = entry.replace(/\.md$/, '').toLowerCase();
      return id === 'readme' ? 'index' : id;
    },
  }),
});

export const collections = { docs };
