#!/usr/bin/env python3
"""Capture independent western Easter vectors over python-dateutil's documented range."""
import hashlib
import inspect
import json
from pathlib import Path
import dateutil
from dateutil.easter import easter, EASTER_WESTERN

assert dateutil.__version__ == "2.9.0.post0"
root = Path(__file__).resolve().parents[1]
source = Path(inspect.getfile(easter))
payload = {
    "oracle": "python-dateutil",
    "version": dateutil.__version__,
    "source": "https://github.com/dateutil/dateutil/blob/2.9.0.post0/src/dateutil/easter.py",
    "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "method": "EASTER_WESTERN",
    "range": [1583, 4099],
    "dates": [easter(year, EASTER_WESTERN).isoformat() for year in range(1583, 4100)],
}
path = root / "tests/Boutquin.MarketData.Tests.Integration/Fixtures/easter-oracle.json"
path.write_text(json.dumps(payload, separators=(",", ":")) + "\n", encoding="utf-8")
print(json.dumps({"vectors": len(payload["dates"]), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}))
