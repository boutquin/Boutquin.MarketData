"""The fingerprinted API declarations must check out byte-identically on every OS."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ApiBaselineCheckoutTests(unittest.TestCase):
    def test_windows_style_checkout_preserves_fingerprinted_api_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', '-q', '-b', 'main', str(root)], check=True)
            subprocess.run(['git', 'config', 'core.autocrlf', 'true'], cwd=root, check=True)
            (root / '.gitattributes').write_bytes((ROOT / '.gitattributes').read_bytes())
            inputs = {}
            for original in sorted((ROOT / 'src').glob('*/PublicAPI.*.txt')):
                relative = original.relative_to(ROOT)
                destination = root / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                inputs[relative] = original.read_bytes()
                destination.write_bytes(inputs[relative])
            self.assertEqual(24, len(inputs))
            subprocess.run(['git', 'add', '--', '.gitattributes', *map(str, inputs)], cwd=root, check=True,
                           capture_output=True)
            for relative in inputs:
                (root / relative).unlink()
            subprocess.run(['git', 'checkout-index', '--force', '--all'], cwd=root, check=True)
            for relative, expected in inputs.items():
                with self.subTest(path=str(relative)):
                    self.assertEqual(expected, (root / relative).read_bytes())
