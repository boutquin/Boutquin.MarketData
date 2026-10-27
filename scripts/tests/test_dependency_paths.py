import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("dependency_check", Path(__file__).parents[1] / "check-runner-dependencies.py")
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)

class DependencyPathTests(unittest.TestCase):
    def test_paths_cannot_escape_pinned_checkout(self):
        for field in ("packageDirectory", "manifest"):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                feed = root / "feed"
                feed.mkdir()
                (root / "dependency-source.json").write_text(json.dumps({"revision": "source", field: "../outside"}))
                with patch.object(check, "ROOT", root), patch.object(check.subprocess, "check_output", return_value="source\n"):
                    with self.assertRaisesRegex(ValueError, "inside"):
                        check.verify(feed)

if __name__ == "__main__":
    unittest.main()
