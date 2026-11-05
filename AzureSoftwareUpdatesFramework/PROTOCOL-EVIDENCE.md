# ADR Software Updates protocol evidence

Implementation baseline: Azure IoT SDK commit `3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5`, inspected 30 October 2026.

- Design contract: [C SDK software-updates.md](https://raw.githubusercontent.com/Azure/azure-iot-sdk/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/docs/eng/software-updates.md)
- Regular service sample and published root-key handling: [simulated_regular README](https://raw.githubusercontent.com/Azure/azure-iot-sdk/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/samples/software_update/pc/simulated_regular/README.md)
- DPS channel fixtures: [su_channel_dps_test.c](https://raw.githubusercontent.com/Azure/azure-iot-sdk/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/tests/unit/su_channel_dps_test.c)
- Nested JWS vectors and supported manifest shape: [su_crypto_vectors.h](https://raw.githubusercontent.com/Azure/azure-iot-sdk/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/tests/support/su_crypto_vectors.h)
- Public operation schema: [DPS device.json](https://github.com/Azure/azure-rest-api-specs/blob/main/specification/deviceprovisioningservices/data-plane/DeviceProvisioningServices/preview/2026-11-02-preview/device.json)
- The pinned .NET matrix states that Software Updates over DPS are unsupported: [dotnet README](https://github.com/Azure/azure-iot-sdk/blob/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/dotnet/README.md)

Confirmed wire behavior used here:

- The regular request is MQTT `PUBLISH $dps/registrations/POST/iotdps-get-deviceupdate/?$rid=...`, followed by response correlation on `$dps/registrations/res/#`; reporting uses `iotdps-report-deviceupdatestatus`.
- API version is `2026-11-02-preview`. The client sends agent info (unless an agent-info ETag is cached), the installed ID when known, and cached ETags.
- A successful check with omitted `updateMetadata` means no offer. `Retry-After` arrives as `retry-after` on the DPS response topic. Report success is a 2xx acknowledgement; a report 409 is never treated as accepted.
- `updateManifestSignature` is a compact RS256 JWS whose protected header embeds an `sjwk` compact JWS. The SJWK header `kid` resolves only against the operator-provided root baseline; its payload supplies the manifest signing JWK. The signed manifest JWS payload contains base64 SHA-256 of the exact unescaped manifest string.
- The signed manifest file hash—not `fileUrls`—authenticates download bytes. Service root-key URLs are not an independent trust anchor.

Important subset distinction: the C SDK design supports more lifecycle and platform hooks than this host. This implementation deliberately supports only a regular, single-step Skittle Sorter application update and does not claim full C SDK, Azure, or production-agent conformance.
