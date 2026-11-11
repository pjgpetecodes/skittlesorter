"""Reset one configured demo device using Azure CLI, without PowerShell."""

import argparse
import json
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path


def required_string(section, name):
    value = section.get(name)
    if not isinstance(value, str) or not value.strip() or "<" in value or ">" in value:
        raise ValueError(f"Configuration must define '{name}' with a real value.")
    return value


def run_azure(subscription, arguments):
    operation = " ".join(arguments[:next(
        (index for index, value in enumerate(arguments) if value.startswith("-")),
        len(arguments),
    )])
    print(f"[{datetime.now():%H:%M:%S}] Running az {operation}...", flush=True)
    started = time.monotonic()
    result = subprocess.run(
        ["az", *arguments, "--subscription", subscription,
         "--only-show-errors", "--output", "json"],
        capture_output=True, text=True, timeout=180,
    )
    print(
        f"[{datetime.now():%H:%M:%S}] az {operation} returned after "
        f"{time.monotonic() - started:.1f}s.",
        flush=True,
    )
    result.check_returncode()
    return json.loads(result.stdout) if result.stdout.strip() else None


def hub_device_exists(subscription, resource_group, hub, device, auth_type):
    try:
        found = run_azure(subscription, [
            "iot", "hub", "device-identity", "show",
            "-g", resource_group, "--hub-name", hub,
            "--device-id", device, "--auth-type", auth_type, "--query", "deviceId",
        ])
    except subprocess.CalledProcessError as error:
        message = (error.stdout or "") + (error.stderr or "")
        if "ErrorCode:DeviceNotFound;404" in message:
            return False
        raise
    if found != device:
        raise ValueError("Hub identity does not match the exact reset target.")
    return True


def assignment_path(config_path, dps, device):
    base = config_path.parent
    for ancestor in (base, *base.parents):
        if any(ancestor.glob("*.csproj")):
            base = ancestor
            break
    issued = dps.get("IssuedCertFilePath") or str(Path("certs", "issued", "issued.pem"))
    if not isinstance(issued, str):
        raise ValueError("IssuedCertFilePath must be a string.")
    issued = re.sub(r"\{RegistrationId\}", device, issued, flags=re.IGNORECASE)
    issued_path = Path(issued.replace("\\", "/"))
    if not issued_path.is_absolute():
        issued_path = base / issued_path
    cache = Path(str(issued_path) + ".connection.json")
    if cache.is_symlink() or (cache.exists() and not cache.is_file()):
        raise ValueError(f"Refusing to remove a non-regular assignment file: {cache}")
    return cache


def reset(args):
    config_path = Path(args.config).resolve(strict=True)
    configuration = json.loads(config_path.read_text())
    dps = configuration["IoTHub"]["DpsProvisioning"]
    adr = configuration["Adr"]
    if not isinstance(dps, dict) or not isinstance(adr, dict):
        raise ValueError("The DPS and ADR configuration sections must be objects.")
    device = required_string(dps, "RegistrationId")
    scope = required_string(dps, "IdScope")
    subscription = required_string(adr, "SubscriptionId")
    resource_group = required_string(adr, "ResourceGroupName")
    namespace = required_string(adr, "NamespaceName")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,127}", device):
        raise ValueError("RegistrationId must be a lowercase DPS registration ID.")
    cache = assignment_path(config_path, dps, device)
    target = f"{subscription}/{resource_group}/{namespace}/{device}"
    print(f"Reset target: {target}", flush=True)
    print(
        f"DPS: {args.dps_name}; Hub: {args.hub_name}; "
        f"enrollment: {args.enrollment_id}; Hub authentication: {args.hub_auth_type}",
        flush=True,
    )
    if args.what_if:
        print(f"Would remove this device's ADR, Hub and DPS records and cache: {cache}")
        print("Enrollment, infrastructure, bootstrap and operational certificates/keys retained.")
        return

    def azure(arguments):
        return run_azure(subscription, arguments)

    service_scope = azure([
        "iot", "dps", "show", "-g", resource_group, "-n", args.dps_name,
        "--query", "properties.idScope",
    ])
    if service_scope != scope:
        raise ValueError("Configuration ID scope does not match the specified DPS service.")
    enrollment = azure([
        "iot", "dps", "enrollment-group", "show", "-g", resource_group,
        "--dps-name", args.dps_name, "--enrollment-id", args.enrollment_id,
        "--query", "{id:enrollmentGroupId,namespace:namespaceName}",
    ])
    if enrollment["id"] != args.enrollment_id or enrollment["namespace"] != namespace:
        raise ValueError("Enrollment group does not match the specified ADR namespace.")
    registrations = azure([
        "iot", "dps", "enrollment-group", "registration", "list", "-g", resource_group,
        "--dps-name", args.dps_name, "--enrollment-id", args.enrollment_id,
        "--query", "[].registrationId",
    ])
    if not isinstance(registrations, list) or device not in registrations:
        raise ValueError("Configured device is not registered in the specified enrollment group.")
    hub = azure([
        "iot", "hub", "show", "-g", resource_group, "-n", args.hub_name,
        "--query", "name",
    ])
    if hub != args.hub_name:
        raise ValueError("The specified IoT Hub was not found.")
    state = azure([
        "iot", "dps", "enrollment-group", "registration", "show", "-g", resource_group,
        "--dps-name", args.dps_name, "--registration-id", device,
        "--query", "{id:registrationId,device:deviceId,hub:assignedHub}",
    ])
    if (state["id"] != device or state["device"] != device or state["hub"] not in (
        f"{args.hub_name}.azure-devices.net", f"{args.hub_name}.device.azure-devices.net",
    )):
        raise ValueError("DPS registration/device/Hub assignment does not match the target.")
    devices = azure([
        "iot", "adr", "ns", "device", "list", "-g", resource_group,
        "--namespace", namespace, "--query", "[].name",
    ])
    if not isinstance(devices, list):
        raise ValueError("ADR device inventory must be a list.")
    exists = hub_device_exists(
        subscription, resource_group, args.hub_name, device, args.hub_auth_type,
    )
    if not exists and device in devices:
        raise ValueError("Hub identity is missing while ADR device exists; investigate first.")

    print("Verified exact target. Only device records and the cached assignment will be deleted.")
    if not args.yes and input(f"Reset '{device}'? [y/N] ").strip().lower() not in ("y", "yes"):
        print("Reset cancelled; no changes made.")
        return
    if device in devices:
        azure([
            "iot", "adr", "ns", "device", "delete", "-g", resource_group,
            "--namespace", namespace, "-n", device, "--yes", "--no-wait",
        ])
        azure([
            "iot", "adr", "ns", "device", "wait", "-g", resource_group,
            "--namespace", namespace, "-n", device, "--deleted", "--timeout", "120",
        ])
    if hub_device_exists(
        subscription, resource_group, args.hub_name, device, args.hub_auth_type,
    ):
        azure([
            "iot", "hub", "device-identity", "delete", "-g", resource_group,
            "--hub-name", args.hub_name, "--device-id", device,
            "--auth-type", args.hub_auth_type,
        ])
    azure([
        "iot", "dps", "enrollment-group", "registration", "delete", "-g", resource_group,
        "--dps-name", args.dps_name, "--registration-id", device,
    ])
    cache.unlink(missing_ok=True)
    print(f"Clean demo start prepared for '{device}'. Enrollment and certificates/keys retained.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", default="appsettings.json")
    parser.add_argument("--dps-name", required=True)
    parser.add_argument("--hub-name", required=True)
    parser.add_argument("--enrollment-id", required=True)
    parser.add_argument("--hub-auth-type", choices=("login", "key"), default="login")
    parser.add_argument("--what-if", action="store_true", help="Preview without Azure requests.")
    parser.add_argument("--yes", action="store_true", help="Skip confirmation, not target checks.")
    args = parser.parse_args(argv)
    try:
        reset(args)
    except subprocess.CalledProcessError as error:
        print(f"Reset failed: {(error.stderr or error.stdout or str(error)).strip()}", file=sys.stderr)
        return 1
    except subprocess.TimeoutExpired as error:
        print(f"Reset failed: Azure CLI exceeded its {error.timeout}s command timeout.", file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError, TypeError, EOFError) as error:
        print(f"Reset failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
