"""Keep local article drafts/WordPress exports paired without any publishing credentials."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SERIES = ("device-update-for-iot-hub", "adr-software-updates")


class ArticleSynchronizationTests(unittest.TestCase):
    def test_both_series_have_six_posts_with_identical_markdown_drafts_and_local_html(self):
        for series in SERIES:
            source = ROOT / "blog-remote" / series
            articles = sorted(source.glob("*.md"))
            self.assertEqual(7, len(articles), series)
            for article in articles:
                with self.subTest(article=article.name, series=series):
                    text = article.read_text(encoding="utf-8-sig")
                    self.assertEqual(text, (ROOT / "blog" / series / article.name).read_text(encoding="utf-8-sig"))
                    html = article.with_suffix(".html").read_text(encoding="utf-8-sig")
                    self.assertIn("1.0.0b2", html)
                    self.assertIn("6 October 2026", html)
                    self.assertIn("30 October 2026", html)
                    self.assertIn("<!-- wp:quote -->", html)
                    for target in re.findall(r"\]\(([^)]+)\)", text):
                        if "://" not in target and not target.startswith("#"):
                            self.assertTrue((article.parent / target.split("#")[0]).exists(), target)
                    blocks = []
                    for match in re.finditer(r"<!-- (/?)wp:([\w/-]+)(.*?)-->", html):
                        closing, block, attributes = match.groups()
                        if closing:
                            self.assertTrue(blocks, article.name)
                            self.assertEqual(block, blocks.pop())
                        elif not attributes.rstrip().endswith("/"):
                            blocks.append(block)
                    self.assertFalse(blocks, article.name)


if __name__ == "__main__":
    unittest.main()
