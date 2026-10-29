"""Publish bijiSDK configs into local development storage."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from store import PublicationStore, read_json


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--data-dir", type=Path,
                        default=Path(__file__).resolve().parents[1] / "dev-data")
    commands = result.add_subparsers(dest="command", required=True)

    demo = commands.add_parser("init-demo", help="Publish sample configs for Editor, Android, and iOS.")
    demo.add_argument("--author", default="local")
    demo.add_argument("--project", default="bijisdk-offline-sample")
    demo.add_argument("--environment", default="development")
    demo.add_argument("--reason", default="Initialize local development sample.")

    publish = commands.add_parser("publish")
    publish.add_argument("--file", type=Path, required=True)
    publish.add_argument("--reason", required=True)
    publish.add_argument("--author", default="local")
    publish.add_argument("--platform", help="Override platform before publishing a new revision.")
    publish.add_argument("--validate-example-features", action="store_true",
                         help="Validate the sample energy/interstitial schemas as well as the envelope.")
    publish.add_argument("--expected-revision", help="Reject publication if the current pointer changed.")

    rollback = commands.add_parser("rollback")
    rollback.add_argument("--project", required=True)
    rollback.add_argument("--environment", required=True)
    rollback.add_argument("--platform", required=True)
    rollback.add_argument("--contract-version", type=int, default=1)
    rollback.add_argument("--revision", required=True, help="Previously published revision to copy.")
    rollback.add_argument("--reason", required=True)
    rollback.add_argument("--author", default="local")
    rollback.add_argument("--expected-revision")
    return result


def main(argv=None):
    args = parser().parse_args(argv)
    store = PublicationStore(args.data_dir)
    try:
        if args.command == "init-demo":
            project_root = Path(__file__).resolve().parents[2]
            template = read_json(project_root / "contracts" / "examples" / "valid-config.json")
            template["projectId"] = args.project
            template["environment"] = args.environment
            result = [store.publish(template, args.reason, args.author, platform=platform,
                                    example_features=True)
                      for platform in ("editor", "android", "ios")]
        elif args.command == "publish":
            result = store.publish(read_json(args.file), args.reason, args.author,
                                   platform=args.platform,
                                   example_features=args.validate_example_features,
                                   expected_revision=args.expected_revision)
        else:
            result = store.rollback(args.project, args.environment, args.platform,
                                    args.contract_version, args.revision, args.reason,
                                    args.author, args.expected_revision)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Publication failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
