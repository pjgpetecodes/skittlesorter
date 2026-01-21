# Security

Best practices for credentials and certificates.

## Credentials
- Use Azure AD roles and managed identities where possible
- Avoid embedding secrets in repo; prefer Key Vault

## Certificates
- Store securely, rotate regularly
- Limit filesystem permissions

## Operational
- Audit ADR and IoT Hub actions
- Enable diagnostics logging

## Related

- [Azure Setup](./Azure-Setup.md)
- [Configuration](./Configuration.md)