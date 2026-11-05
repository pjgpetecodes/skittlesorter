# Remote-synced WordPress articles

These are separate working copies imported from the live WordPress posts. The
Markdown under `blog\` is maintained separately and isn't overwritten by this
sync workflow. Each article has its downloaded raw Gutenberg HTML, a
remote-content baseline, a per-article config, and a local sync baseline in
`.wp-md\`.

## Workflow

1. Edit the matching Markdown file here.
2. Preview the local Markdown/HTML sync:

   ```powershell
   node C:\repos\petecodes\wordpress-markdown-sync\bin\wp-md.mjs sync --config C:\repos\skittlesorter\blog-remote\00-introduction.wp-md.json
   ```

3. Review the plan, then apply it locally with `sync --apply`. If an
   intentional rewrite changes the Gutenberg block structure and sync refuses,
   review the Markdown and regenerate its paired HTML locally with
   `wp-md export <markdown> <html> --force`. Export creates a backup and does
   not contact WordPress.
4. Preview the WordPress update:

   ```powershell
   node C:\repos\petecodes\wordpress-markdown-sync\bin\wp-md.mjs push --config C:\repos\skittlesorter\blog-remote\00-introduction.wp-md.json --credentials-file C:\repos\petecodes\wordpress-markdown-sync\.env
   ```

5. Only after reviewing the preview, add `--apply` to `push` to update that
   WordPress post. These posts are published, so an applied push changes the
   live article.

Use the corresponding config filename for each article. Do not copy these
working files over the original `blog\` drafts. The remote baseline makes push
refuse when WordPress content has changed since the last pull/push; preview
again and reconcile instead of bypassing that check.

## Import caveats

The Markdown is an import of raw Gutenberg content, not a hand-cleaned draft.
Plugin blocks are retained as `wordpress-block` fences; preserve them unless
you deliberately intend to replace them. Code Block Pro content imports as
ordinary Markdown code, so editing those blocks may lose plugin-specific
formatting. Inspect opaque blocks and code before applying changes. The Part 5
import also contains unusable VS Code resource links and HTML entities that
should be corrected before publishing affected sections.
