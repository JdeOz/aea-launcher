# Data model
PackRelease(schema1, packId, version, name, minecraft, gameMetadata, fabric, javaArchive, mods,
optional server address); artifacts(url, path, hashAlgorithm, hash, size).
SignedEnvelope(payloadBase64, signatureBase64); publisher retains private key, app public key.
ActiveRelease(envelope); immutable slots identified by payload SHA256, game instance by packId.
Preferences(maximumRamMb). MSAL account cache encrypted CurrentUser; no plaintext account file.
