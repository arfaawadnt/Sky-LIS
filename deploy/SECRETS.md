# Secrets and configuration keys (02 §6.4)

Every LIS key lives under the **`Lis`** configuration root (environment form `Lis__Section__Key`). This host also runs
other applications that set generic machine-wide variables (`ConnectionStrings__Postgres`, `Jwt__Secret`,
`ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`); a generic key name would silently read theirs. LIS therefore never reads
`ConnectionStrings:*` or `Jwt:*`, pins its Kestrel endpoint in `appsettings.json` (`Kestrel:Endpoints`, which takes
precedence over `ASPNETCORE_URLS`), and the start-up guard refuses any database whose name does not start with `lis`
or any role whose name does not start with `lis_` before it connects.

No secret lives in `appsettings*.json`. Every value below arrives through user-secrets (development), environment
variables or the deployment's secret store. The start-up guard refuses to run when one is missing or weak, under every
environment name.

| Key | Form | Generate |
| --- | --- | --- |
| `Lis:Database:ConnectionString` | runtime role connection string (`lis_app`, NOBYPASSRLS, database `lis…`) | password from `deploy/postgres/.env` (dev) or the vault |
| `Lis:Jwt:SigningKey` | P-256 private key, PEM (ES256) | `node -e "const {generateKeyPairSync}=require('crypto');console.log(generateKeyPairSync('ec',{namedCurve:'P-256'}).privateKey.export({type:'pkcs8',format:'pem'}))"` |
| `Lis:Jwt:SigningKeyId` | optional key id; derived from the public key when absent | — |
| `Lis:Jwt:SubjectEncryptionKey` | 32 random bytes, base64 (D-02) | `node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"` |
| `Lis:Jwt:PreviousSubjectEncryptionKeys:0…` | previous 32-byte keys during a rotation overlap | the key being retired |
| `Lis:DataProtection:MasterKey` | 32 random bytes, base64; protects envelope keys at rest | as above |

Development shortcut (stores everything in the WebApi user-secrets store, prints nothing secret):

```bash
node scripts/new-dev-secrets.mjs
```

Rotation runbooks:

- **Subject encryption key**: add the new key as `Lis:Jwt:SubjectEncryptionKey`, move the old one to
  `Lis:Jwt:PreviousSubjectEncryptionKeys`, deploy, remove the old one after 24 hours.
- **Signing key**: generate a new P-256 key with a new `Lis:Jwt:SigningKeyId`; tokens signed with the old key are
  refused at cut-over (15-minute impact at most); the JWKS document publishes the current key.
- **Master key**: envelope keys sealed under the old master key become unreadable; the key store retires them and
  generates a fresh pair on the next challenge, so outstanding challenges (≤ 120 s) fail once and clients retry.
