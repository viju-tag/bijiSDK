"""Validated, immutable local publications for the bijiSDK development server."""
from __future__ import annotations

import copy
import hashlib
import json
import math
import os
import re
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path

MAX_BYTES = 1_048_576
MAX_INTEGER = 9_007_199_254_740_991
IDENTIFIER = re.compile(r"[a-z][a-z0-9-]*")
REVISION = re.compile(r"release-[0-9a-f]{32}")


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def _reject_constant(value):
    raise ValueError(f"Non-finite JSON number: {value}")


def read_json(path: Path, max_bytes: int = MAX_BYTES):
    with path.open("rb") as stream:
        data = stream.read(max_bytes + 1)
    if len(data) > max_bytes:
        raise ValueError("JSON document exceeds the size limit.")
    return json.loads(data.decode("utf-8"), object_pairs_hook=_unique_object,
                      parse_constant=_reject_constant)


def encoded(value) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                      allow_nan=False).encode("utf-8")


def identifier(value, name):
    if not isinstance(value, str) or IDENTIFIER.fullmatch(value) is None:
        raise ValueError(f"{name} must be a lowercase identifier.")
    return value


def integer(value, name, minimum=0):
    if type(value) is not int or not minimum <= value <= MAX_INTEGER:
        raise ValueError(f"{name} must be an integer from {minimum} to {MAX_INTEGER}.")
    return value


def _validate_tree(value, depth=0):
    if depth > 64:
        raise ValueError("JSON nesting exceeds 64 levels.")
    if isinstance(value, dict):
        for child in value.values():
            _validate_tree(child, depth + 1)
    elif isinstance(value, list):
        for child in value:
            _validate_tree(child, depth + 1)
    elif type(value) is int and abs(value) > MAX_INTEGER:
        raise ValueError("Integer exceeds the interoperable JSON range.")
    elif type(value) is float and not math.isfinite(value):
        raise ValueError("Non-finite numbers are not allowed.")


def validate_response(response, example_features=False):
    if not isinstance(response, dict):
        raise ValueError("Response must be an object.")
    if type(response.get("schemaVersion")) is not int or response["schemaVersion"] != 1:
        raise ValueError("schemaVersion must be 1.")
    for key in ("projectId", "environment", "platform"):
        identifier(response.get(key), key)
    integer(response.get("configContractVersion"), "configContractVersion", 1)
    if not isinstance(response.get("revision"), str) or not response["revision"].strip():
        raise ValueError("revision must be a nonblank string.")
    timestamp = response.get("publishedAt")
    if not isinstance(timestamp, str) or not timestamp.endswith("Z"):
        raise ValueError("publishedAt must be a UTC timestamp ending in Z.")
    try:
        parsed = datetime.fromisoformat(timestamp[:-1] + "+00:00")
        if parsed.tzinfo is None:
            raise ValueError("Timestamp must include a time and UTC offset.")
    except ValueError as error:
        raise ValueError("publishedAt is not a valid timestamp.") from error
    bounds = response.get("compatibility")
    if not isinstance(bounds, dict) or "maxAppBuild" not in bounds:
        raise ValueError("compatibility must include minAppBuild and maxAppBuild.")
    minimum = integer(bounds.get("minAppBuild"), "minAppBuild")
    maximum = bounds["maxAppBuild"]
    if maximum is not None:
        integer(maximum, "maxAppBuild")
        if maximum < minimum:
            raise ValueError("maxAppBuild cannot be less than minAppBuild.")
    if not isinstance(response.get("values"), dict):
        raise ValueError("values must be an object.")
    _validate_tree(response)
    if len(encoded(response)) > MAX_BYTES:
        raise ValueError("Response exceeds the size limit.")
    if example_features:
        validate_example_values(response["values"])
    return response


def validate_example_values(values):
    """Optional illustrative validators; generic publication imposes no game schema."""
    if "energy" in values:
        energy = values["energy"]
        if not isinstance(energy, dict):
            raise ValueError("energy must be an object.")
        integer(energy.get("maximum"), "energy.maximum", 1)
        integer(energy.get("refillSeconds"), "energy.refillSeconds", 1)
    if "interstitial" in values:
        ads = values["interstitial"]
        if not isinstance(ads, dict) or type(ads.get("enabled")) is not bool:
            raise ValueError("interstitial.enabled must be a boolean.")
        integer(ads.get("cooldownSeconds"), "interstitial.cooldownSeconds")


def context(project, environment, platform, contract):
    return (identifier(project, "projectId"), identifier(environment, "environment"),
            identifier(platform, "platform"), integer(contract, "configContractVersion", 1))


class PublicationStore:
    def __init__(self, data_dir):
        self.data_dir = Path(data_dir).resolve()

    def _directory(self, ctx):
        digest = hashlib.sha256(encoded(list(ctx))).hexdigest()
        return self.data_dir / "contexts" / digest

    def _read_index(self, directory):
        path = directory / "index.json"
        if not path.exists():
            return []
        document = read_json(path, 16 * MAX_BYTES)
        if not isinstance(document, dict) or document.get("formatVersion") != 1:
            raise ValueError("Unsupported publication index format.")
        revisions = document.get("revisions")
        if not isinstance(revisions, list) or any(
                not isinstance(rev, str) or REVISION.fullmatch(rev) is None for rev in revisions):
            raise ValueError("Invalid publication index.")
        return revisions

    @contextmanager
    def _locked(self, directory):
        directory.mkdir(parents=True, exist_ok=True)
        lock_path = directory / "publish.lock"
        try:
            descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError as error:
            raise ValueError("Publication is locked. Check for another publisher or a stale lock.") from error
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                stream.write(str(os.getpid()))
                stream.flush()
                os.fsync(stream.fileno())
            yield
        finally:
            lock_path.unlink(missing_ok=True)

    @staticmethod
    def _write_new(path, data):
        with path.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())

    def publish(self, response, reason, author="local", platform=None,
                example_features=False, expected_revision=None, rollback_from=None):
        candidate = copy.deepcopy(response)
        if platform is not None:
            candidate["platform"] = platform
        validate_response(candidate, example_features)
        if not isinstance(reason, str) or not reason.strip():
            raise ValueError("A publication reason is required.")
        if not isinstance(author, str) or not author.strip():
            raise ValueError("An author is required.")
        if len(encoded({"author": author, "reason": reason})) > 4096:
            raise ValueError("Publication author/reason metadata exceeds 4 KiB.")
        ctx = context(candidate["projectId"], candidate["environment"],
                      candidate["platform"], candidate["configContractVersion"])
        directory = self._directory(ctx)
        with self._locked(directory):
            history = self._read_index(directory)
            previous = history[0] if history else None
            if expected_revision is not None and previous != expected_revision:
                raise ValueError("Active publication changed; expected revision does not match.")
            revision = "release-" + uuid.uuid4().hex
            published_at = datetime.now(timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")
            candidate["revision"] = revision
            candidate["publishedAt"] = published_at
            validate_response(candidate, example_features)
            audit = {"author": author, "reason": reason, "publishedAt": published_at,
                     "previousRevision": previous, "rollbackFrom": rollback_from}
            publications = directory / "publications"
            publications.mkdir(exist_ok=True)
            self._write_new(publications / (revision + ".json"),
                            encoded({"response": candidate, "audit": audit}))
            index_temp = directory / ("index-" + uuid.uuid4().hex + ".tmp")
            try:
                index_bytes = encoded({"formatVersion": 1, "revisions": [revision] + history})
                if len(index_bytes) > 16 * MAX_BYTES:
                    raise ValueError("Development publication history exceeds 16 MiB.")
                self._write_new(index_temp, index_bytes)
                os.replace(index_temp, directory / "index.json")
            finally:
                index_temp.unlink(missing_ok=True)
        return {"revision": revision, "previousRevision": previous,
                "projectId": ctx[0], "environment": ctx[1], "platform": ctx[2],
                "configContractVersion": ctx[3], "publishedAt": published_at}

    def _publication(self, directory, revision, ctx):
        document = read_json(directory / "publications" / (revision + ".json"), MAX_BYTES + 8192)
        response = document["response"]
        validate_response(response)
        actual = context(response["projectId"], response["environment"],
                         response["platform"], response["configContractVersion"])
        if actual != ctx or response["revision"] != revision:
            raise ValueError("Publication context or revision mismatch.")
        return response

    def select(self, project, environment, platform, app_build, contract):
        ctx = context(project, environment, platform, contract)
        integer(app_build, "appBuild")
        directory = self._directory(ctx)
        for revision in self._read_index(directory):
            response = self._publication(directory, revision, ctx)
            bounds = response["compatibility"]
            if app_build >= bounds["minAppBuild"] and (
                    bounds["maxAppBuild"] is None or app_build <= bounds["maxAppBuild"]):
                return response
        return None

    def rollback(self, project, environment, platform, contract, revision,
                 reason, author="local", expected_revision=None):
        ctx = context(project, environment, platform, contract)
        directory = self._directory(ctx)
        if revision not in self._read_index(directory):
            raise ValueError("Rollback revision is not in this context's published history.")
        response = self._publication(directory, revision, ctx)
        return self.publish(response, reason, author, expected_revision=expected_revision,
                            rollback_from=revision)
