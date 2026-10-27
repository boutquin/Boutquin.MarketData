import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('consumer', Path(__file__).parents[1] / 'qualify-rc-consumers.py')
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)

class ConsumerLockTests(unittest.TestCase):
    def test_only_exact_kernel_version_and_hash_changes_are_accepted(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            feed = root / 'feed'; feed.mkdir()
            name = 'Boutquin.MarketData.Abstractions'
            content = b'frozen archive'
            (feed / (name + '.2.0.0-rc.2.nupkg')).write_bytes(content)
            original = {'dependencies': {'net10.0': {name: {'resolved': '2.0.0-rc.1', 'contentHash': 'old'}, 'Other': {'resolved': '1.0.0'}}}}
            current = json.loads(json.dumps(original).replace('2.0.0-rc.1', '2.0.0-rc.2'))
            current['dependencies']['net10.0'][name]['contentHash'] = base64.b64encode(hashlib.sha512(content).digest()).decode()
            path = root / 'packages.lock.json'; path.write_text(json.dumps(current))
            with patch.object(check.subprocess, 'check_output', return_value=json.dumps(original)):
                check.validate_adapter_lock_changes(root, {'packages.lock.json'}, '2.0.0-rc.1', '2.0.0-rc.2', feed)
                current['dependencies']['net10.0']['Other']['resolved'] = '2.0.0'
                path.write_text(json.dumps(current))
                with self.assertRaisesRegex(ValueError, 'beyond exact'):
                    check.validate_adapter_lock_changes(root, {'packages.lock.json'}, '2.0.0-rc.1', '2.0.0-rc.2', feed)
                with self.assertRaisesRegex(ValueError, 'Unexpected changed'):
                    check.validate_adapter_lock_changes(root, {'Production.cs'}, '2.0.0-rc.1', '2.0.0-rc.2', feed)
