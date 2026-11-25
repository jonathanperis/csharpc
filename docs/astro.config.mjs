import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const isProd = process.env.NODE_ENV === 'production';
const base = isProd ? '/csharpc' : '';
const docsDir = path.dirname(fileURLToPath(import.meta.url));
const repoDir = path.dirname(docsDir);
const repoBlob = 'https://github.com/jonathanperis/csharpc/blob/main/';
const repoTree = 'https://github.com/jonathanperis/csharpc/tree/main/';

/** The site route of a markdown file under docs/ ("05-testing.md" → /docs/05-testing/, "README.md" → /docs/). */
export function routeOf(relative) {
  const slug = relative.replace(/\.md$/, '').replace(/(^|\/)README$/, '');
  return `${base}/docs/${slug ? slug.toLowerCase() + '/' : ''}`;
}

/**
 * The docs are written to be read on GitHub, with relative links between them (`05-testing.md#ci`) and into the
 * repository (`../src/runtime/eh.c`). On the site a link to another document goes to its page, and a link to
 * anything else in the repository goes to GitHub, so both readings keep working from the same files.
 */
function rehypeRepoLinks() {
  return (tree, file) => {
    const from = file.path ? path.dirname(file.path) : docsDir;
    const visit = (node) => {
      if (node.type === 'element' && node.tagName === 'a' && typeof node.properties?.href === 'string') {
        const href = node.properties.href;
        if (!/^(?:[a-z]+:|#|\/)/i.test(href)) {
          const [target, hash = ''] = href.split('#');
          const absolute = path.resolve(from, decodeURIComponent(target));
          const anchor = hash ? '#' + hash : '';
          if (absolute.startsWith(docsDir + path.sep) && absolute.endsWith('.md')) {
            node.properties.href = routeOf(path.relative(docsDir, absolute).split(path.sep).join('/')) + anchor;
          } else if (absolute.startsWith(repoDir)) {
            const relative = path.relative(repoDir, absolute).split(path.sep).join('/');
            node.properties.href = (/\.[a-z0-9]+$/i.test(relative) ? repoBlob : repoTree) + relative + anchor;
          }
        }
      }
      node.children?.forEach(visit);
    };
    visit(tree);
  };
}

export default defineConfig({
  integrations: [sitemap()],
  output: 'static',
  outDir: 'out',
  site: 'https://jonathanperis.github.io',
  base: isProd ? '/csharpc' : '/',
  trailingSlash: 'always',
  // One small stylesheet: inline it so each page is a single request.
  build: { inlineStylesheets: 'always' },
  markdown: {
    rehypePlugins: [rehypeRepoLinks],
    shikiConfig: { themes: { light: 'github-light', dark: 'github-dark' }, wrap: false },
  },
});
