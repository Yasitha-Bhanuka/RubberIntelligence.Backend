# Digital Product Passport (DPP) Module — Full-Stack Technical Reference

> **Research Context:** Privacy-Preserving Field-Level Confidentiality for Rubber Trade Documents  
> **Stack:** ASP.NET Core 8 (.NET backend) + React Native / Expo (mobile frontend)  
> **Branch:** `MithunUIdpp` (Backend & Frontend)

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Repository Layout](#2-repository-layout)
3. [Backend Architecture](#3-backend-architecture)
   - 3.1 [Controllers](#31-controllers)
   - 3.2 [Services — Research Contributions](#32-services--research-contributions)
   - 3.3 [Models](#33-models)
   - 3.4 [DTOs](#34-dtos)
4. [Frontend Architecture](#4-frontend-architecture)
   - 4.1 [Screens](#41-screens)
   - 4.2 [Services (API layer)](#42-services-api-layer)
   - 4.3 [TypeScript Types](#43-typescript-types)
5. [API Endpoints](#5-api-endpoints)
6. [End-to-End Pipeline](#6-end-to-end-pipeline)
7. [Security Properties](#7-security-properties)
8. [Research Distinctions vs Prior Art](#8-research-distinctions-vs-prior-art)
9. [Environment & Configuration](#9-environment--configuration)

---

## 1. System Overview

Traditional rubber-trade document systems encrypt entire files or nothing at all. This module introduces a **field-granular privacy architecture** capable of protecting individual key-value fields (e.g., `pricePerKg`, `bankAccount`) within the same document that also carries openly-readable fields (e.g., `rubberGrade`, `quantity`).

Three novel research contributions are implemented:

| # | Contribution | Key file |
|---|---|---|
| 1 | Per-field confidentiality classification | `FieldConfidentialityService.cs` |
| 2 | Selective AES-256-CBC field-level encryption + HMAC blind index | `FieldEncryptionService.cs`, `BlindIndexService.cs` |
| 3 | Confidentiality-aware DPP abstraction (passport never sees financial data) | `DppService.cs` |

The processing is split across two explicit API calls so buyers receive immediate per-field feedback before a passport is generated, and the passport can be regenerated independently.

```
POST /api/dpp/upload                   ← Contributions 1 & 2 (OCR → classify → encrypt)
POST /api/dpp/{dppId}/generate-passport ← Contribution 3 (public-fields-only passport + SHA-256 seal)
```

> **ONNX note:** `dpp_classifier_model_large.onnx` provides a *document-level* classification gate via `OnnxDppService`. The per-field logic (Contributions 1–3) always runs regardless of ONNX availability — keyword heuristics serve as the fallback.

---

## 2. Repository Layout

```
RubberIntelligence.API/
└── Modules/dpp/
    ├── Controllers/
    │   ├── DppController.cs           ← main DPP REST controller
    │   └── MessageController.cs       ← lot-linked secure messaging
    ├── DTOs/
    │   ├── ClassificationResultDto.cs
    │   ├── ConfidentialFieldDto.cs
    │   ├── DocumentUploadRequest.cs
    │   ├── DppVerificationResponseDto.cs
    │   ├── ExporterContextDto.cs
    │   └── MessageDto.cs
    ├── Models/
    │   ├── AccessRequest.cs
    │   ├── DigitalProductPassport.cs
    │   ├── DppDocument.cs
    │   ├── ExtractedField.cs
    │   ├── Message.cs
    │   └── dpp_classifier_model_large.onnx
    └── Services/
        ├── GeminiOcrService.cs
        ├── FieldConfidentialityService.cs
        ├── FieldEncryptionService.cs
        ├── BlindIndexService.cs
        ├── DppDocumentProcessingService.cs
        ├── DppEncryptionService.cs
        ├── DppService.cs
        ├── OnnxDppService.cs
        ├── ConfidentialAccessService.cs
        ├── ExporterContextService.cs
        └── MessageService.cs

rubber-intelligence-app/
└── src/features/dpp/
    ├── index.ts
    ├── screens/
    │   ├── DocumentUploadScreen.tsx
    │   ├── ClassificationResultScreen.tsx
    │   ├── DppPassportScreen.tsx
    │   ├── DppDetailScreen.tsx
    │   ├── ConfidentialAccessScreen.tsx
    │   ├── PendingRequestsScreen.tsx
    │   ├── ExporterScannerScreen.tsx
    │   ├── MarketplaceScreen.tsx
    │   ├── CreateSellingPostScreen.tsx
    │   ├── BuyerDashboardScreen.tsx
    │   ├── BuyerProfileScreen.tsx
    │   ├── LotMessagingScreen.tsx
    │   └── OrderReceiptScreen.tsx
    ├── services/
    │   ├── dppService.ts
    │   ├── marketplaceService.ts
    │   └── messagesService.ts
    └── types/
        └── index.ts
```

---

## 3. Backend Architecture

### 3.1 Controllers

#### `DppController.cs` — `[Route("api/dpp")]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/upload` | Buyer, Admin | Upload document → OCR → classify fields → encrypt confidential fields → return summary |
| `POST` | `/{dppId}/generate-passport` | Buyer, Admin | Build DPP from non-confidential fields only; seal with SHA-256 |
| `GET` | `/passport/{dppId}` | Buyer, Admin | Retrieve existing DPP |
| `GET` | `/my-uploads` | Buyer, Admin | List all documents uploaded by the authenticated user |
| `GET` | `/{id}` | Buyer, Admin | Get single document metadata |
| `GET` | `/{id}/access` | Buyer, Admin | Stream/download encrypted file |
| `POST` | `/request-confidential/{lotId}` | Exporter | Request access to confidential fields of a lot |
| `GET` | `/pending-requests` | Buyer | List pending access requests awaiting approval |
| `POST` | `/approve-confidential/{requestId}` | Buyer | Approve an exporter's confidential access request |
| `GET` | `/confidential/{lotId}` | Exporter | Retrieve decrypted confidential fields (requires approved request) |
| `GET` | `/verify/{lotId}` | Buyer, Exporter | Verify SHA-256 DPP hash integrity |
| `GET` | `/exporter-context/{exporterId}` | Buyer | Fetch exporter identity context |

#### `MessageController.cs` — `[Route("api/messages")]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/{lotId}` | Any authenticated | Send message (optionally AES-256 encrypted if `isConfidential = true`) |
| `GET` | `/{lotId}` | Any authenticated | Retrieve all messages for a lot (only participants can read) |

---

### 3.2 Services — Research Contributions

#### `GeminiOcrService.cs`
Calls **Google Gemini 2.5 Flash** Vision API with a structured prompt, returning a `Dictionary<string, string>` of extracted fields. Accepted MIME types: `image/jpeg`, `image/png`, `image/webp`, `image/gif`, `application/pdf`. Handles `429 Too Many Requests` and propagates it as `HTTP 429` to the client.

#### `FieldConfidentialityService.cs` — **Contribution 1**
Classifies each individual field as CONFIDENTIAL or NON-CONFIDENTIAL using a two-tier keyword corpus:

| Tier | Confidence | Examples |
|------|-----------|---------|
| High (1) | 0.95 | `price`, `bank`, `iban`, `supplier`, `tax id`, `contract number`, `payment` |
| Medium (2) | 0.75 | `address`, `email`, `lot number`, `certificate`, `consignee`, `reference` |
| Non-confidential | 0.80 (public) | `rubberGrade`, `quantity`, `dispatchPort`, `origin` |

`ManualReviewRequired = true` is set for Tier-2 matches. This is structurally novel: prior work classifies at the **document** level — this system classifies at the **field** level.

#### `FieldEncryptionService.cs` — **Contribution 2**
- **Algorithm:** AES-256-CBC (`System.Security.Cryptography.Aes`)
- **IV:** 16-byte CSPRNG IV generated **per field per call** — never reused
- **Key management:** Priority chain — `DPP_FIELD_ENCRYPTION_KEY` env var → `appsettings` → dev fallback
- **Key versioning:** Ciphertext prefixed `v{n}:<base64>` to support future rotation without bulk re-encryption
- **Storage:** `EncryptedValue` and `IV` stored separately in `ExtractedField`; plaintext never persisted

#### `BlindIndexService.cs` — **Contribution 2 (searchability)**
```
HMAC-SHA256(hmacKey, "<fieldName>|<lowercase(value)>") → deterministic URL-safe Base64
```
Enables `WHERE blindIndex = ?` equality searches on encrypted fields without decryption. HMAC key is always separate from the AES key. Scoped per field name to prevent cross-field collisions.

#### `DppDocumentProcessingService.cs`
Orchestrator service — keeps `DppController` free of encryption logic:
1. For each field: calls `FieldConfidentialityService.Classify()`
2. If CONFIDENTIAL → `FieldEncryptionService.Encrypt()` + `BlindIndexService.Compute()`
3. If NON-CONFIDENTIAL → stores plain value, IV = `""`, blindIndex = `null`
4. Bulk-saves `List<ExtractedField>` to MongoDB

#### `DppService.cs` — **Contribution 3**
Constructs the `DigitalProductPassport` using **only non-confidential fields**:
```csharp
var publicFields = allFields.Where(f => !f.IsConfidential);
// pricePerKg, totalAmount, bankAccount ← permanently excluded
```
Seals the passport with `ComputeSha256(json)` where `DppHash` is set to `""` before serialization (reproducible for later verification). `ConfidentialDataExists` is a boolean flag — existence signalled, zero data leaked.

#### `OnnxDppService.cs`
- Loads `dpp_classifier_model_large.onnx` once at startup (Singleton)
- Falls back to keyword heuristics if model file missing or load fails
- Also provides `ProcessAndSecureInvoiceAsync()` — RSA-wrapped AES-CBC file-level encryption keyed to the exporter's RSA public key

#### `ConfidentialAccessService.cs`
Approval-gated decryption:
1. Exporter creates `AccessRequest` (status = PENDING)
2. Buyer explicitly approves → status = APPROVED
3. Service calls `FieldEncryptionService.Decrypt()` only after approval
4. Plaintext returned transiently in HTTP response — never re-persisted

#### `MessageService.cs`
Lot-linked messaging with optional AES-256 encryption (`isConfidential` flag). Only the sender and receiver (participants of the lot) can retrieve messages. Decryption on retrieval is transparent to the caller.

---

### 3.3 Models

| Model | Stored in | Key fields |
|-------|-----------|------------|
| `DigitalProductPassport` | MongoDB | `LotId`, `RubberGrade`, `Quantity`, `DispatchDetails`, `ConfidentialDataExists`, `LifecycleState` (`GENERATED` → `VERIFIED` → `ACCEPTED`/`REJECTED`), `DppHash` |
| `DppDocument` | MongoDB | `OriginalFileName`, `Classification`, `ConfidenceScore`, `DetectedKeywords`, `UploadedBy` |
| `ExtractedField` | MongoDB | `FieldName`, `EncryptedValue`, `IV`, `IsConfidential`, `ConfidenceScore`, `BlindIndex`, `KeyVersion`, `LotId` |
| `AccessRequest` | MongoDB | `LotId`, `ExporterId`, `Status` (PENDING / APPROVED / REJECTED) |
| `Message` | MongoDB | `LotId`, `SenderId`, `ReceiverId`, `Content` (optionally encrypted), `IsConfidential`, `SentAt` |

---

### 3.4 DTOs

| DTO | Direction | Purpose |
|-----|-----------|---------|
| `DocumentUploadRequest` | Inbound | Multipart form: `IFormFile File`, `string? Notes` |
| `ClassificationResultDto` | Outbound | Document-level ONNX result: classification, confidence, keywords |
| `ConfidentialFieldDto` | Outbound | Decrypted field payload (transient, never stored) |
| `DppVerificationResponseDto` | Outbound | `IsValid`, stored hash, recalculated hash |
| `ExporterContextDto` | Outbound | Exporter identity for buyer display |
| `MessageDto` / `SendMessageRequest` | Both | Lot messaging payload |

---

## 4. Frontend Architecture

### 4.1 Screens

All screens live in `rubber-intelligence-app/src/features/dpp/screens/`.

| Screen | Role | Key interactions |
|--------|------|-----------------|
| `DocumentUploadScreen` | Pick image from gallery or camera → call `uploadDppDocument()` → navigate to ClassificationResult or link to order | `expo-image-picker`, multipart POST |
| `ClassificationResultScreen` | Display per-field confidentiality breakdown returned by upload | Shows `isConfidential`, `confidenceScore` per field; navigates to `DppPassport` |
| `DppPassportScreen` | Generate (if not yet created) and display passport; QR code share | Calls `generatePassport()`, shows lifecycle state, SHA-256 hash, `react-native-qrcode-svg` |
| `DppDetailScreen` | View raw document metadata (non-confidential summary) | Calls `getDppMetadata()` |
| `ConfidentialAccessScreen` | Exporter requests / views confidential fields after approval | Calls `requestConfidentialAccess()` then `getConfidentialFields()` |
| `PendingRequestsScreen` | Buyer approves / rejects exporter access requests | Calls `getPendingAccessRequests()`, `approveAccessRequest()` |
| `ExporterScannerScreen` | Exporter scans a DPP QR code to look up a lot | QR scan → `getDppMetadata()` |
| `MarketplaceScreen` | Browse active selling posts; request purchase | Calls marketplace service |
| `CreateSellingPostScreen` | Buyer creates a new selling post with grade, quantity, price | POST to marketplace |
| `BuyerDashboardScreen` | Buyer's overview: uploads, active posts, transactions | Aggregated data |
| `BuyerProfileScreen` | Buyer profile and linked documents | Profile + uploads list |
| `LotMessagingScreen` | Secure messaging between buyer and exporter for a specific lot | Calls `messagesService.ts` |
| `OrderReceiptScreen` | Transaction receipt after purchase completion | Displays `MarketplaceTransaction` data |

---

### 4.2 Services (API layer)

#### `dppService.ts`

| Function | HTTP | Endpoint | Description |
|----------|------|----------|-------------|
| `uploadDppDocument(fileUri, fileName, fileType)` | `POST` | `/dpp/upload` | Multipart upload; returns `DppUploadResponse` |
| `generatePassport(dppId)` | `POST` | `/dpp/{dppId}/generate-passport` | Trigger DPP generation |
| `getPassport(dppId)` | `GET` | `/dpp/passport/{dppId}` | Fetch existing DPP |
| `getBuyerDocuments()` | `GET` | `/dpp/my-uploads` | All uploads for authenticated buyer |
| `getDppMetadata(id)` | `GET` | `/dpp/{id}` | Single document metadata |
| `getDppFileUrl(id)` | — | `/dpp/{id}/access` | Constructs file download URL |
| `requestConfidentialAccess(lotId)` | `POST` | `/dpp/request-confidential/{lotId}` | Exporter requests access |
| `getPendingAccessRequests()` | `GET` | `/dpp/pending-requests` | Buyer: list pending requests |
| `approveAccessRequest(requestId)` | `POST` | `/dpp/approve-confidential/{requestId}` | Buyer approves |
| `getConfidentialFields(lotId)` | `GET` | `/dpp/confidential/{lotId}` | Exporter: fetch decrypted fields |
| `verifyDpp(lotId)` | `GET` | `/dpp/verify/{lotId}` | Verify SHA-256 hash |
| `getExporterContext(exporterId)` | `GET` | `/dpp/exporter-context/{exporterId}` | Exporter context for buyer display |

#### `marketplaceService.ts`
Covers selling post CRUD, purchase requests, status updates, and `linkDppToTransaction()` — links an uploaded DPP document ID to an existing transaction after upload.

#### `messagesService.ts`
Wraps `POST /api/messages/{lotId}` and `GET /api/messages/{lotId}` for lot-level secure messaging.

---

### 4.3 TypeScript Types

Defined in `src/features/dpp/types/index.ts`:

| Interface | Maps to |
|-----------|---------|
| `DppUploadResponse` | `POST /api/dpp/upload` response |
| `DppFieldSummary` | Per-field result inside upload response |
| `DppClassification` | Document-level classification summary |
| `DigitalProductPassport` | `GET/POST passport` endpoints |
| `DppDocument` | `GET /dpp/my-uploads` items |
| `SellingPost` | Marketplace listing |
| `MarketplaceTransaction` | Purchase transaction record |
| `AccessRequest` | Controlled-access request |
| `ConfidentialAccessResponse` | Decrypted field payload from approved access |
| `DppVerificationResponse` | Hash verification result |
| `ExporterContext` | Exporter identity for buyer-side display |
| `Message` | Lot message record |

---

## 5. API Endpoints

### DPP Core

```
POST   /api/dpp/upload                           (Buyer, Admin)
POST   /api/dpp/{dppId}/generate-passport        (Buyer, Admin)
GET    /api/dpp/passport/{dppId}                 (Buyer, Admin)
GET    /api/dpp/my-uploads                       (Buyer, Admin)
GET    /api/dpp/{id}                             (Buyer, Admin)
GET    /api/dpp/{id}/access                      (Buyer, Admin)
```

### Controlled Access

```
POST   /api/dpp/request-confidential/{lotId}     (Exporter)
GET    /api/dpp/pending-requests                 (Buyer)
POST   /api/dpp/approve-confidential/{requestId} (Buyer)
GET    /api/dpp/confidential/{lotId}             (Exporter — approved only)
```

### Integrity & Context

```
GET    /api/dpp/verify/{lotId}                   (Buyer, Exporter)
GET    /api/dpp/exporter-context/{exporterId}    (Buyer)
```

### Messaging

```
POST   /api/messages/{lotId}                     (Authenticated)
GET    /api/messages/{lotId}                     (Authenticated — participants only)
```

---

## 6. End-to-End Pipeline

### Step A — `POST /api/dpp/upload`  *(Contributions 1 & 2)*

```
Client (DocumentUploadScreen)
  │  multipart/form-data (image/pdf)
  ▼
DppController.UploadDocument()
  │
  ├─ 1. Validate MIME type (JPEG, PNG, WEBP, GIF, PDF) → 415 if invalid
  │      Save temp file → Uploads/Dpp/<guid>.ext
  │
  ├─ 2. GeminiOcrService.ExtractFieldsAsync()
  │      Gemini 2.5 Flash → Dictionary<string,string>
  │        { "rubberGrade":"RSS3", "quantity":"500", "pricePerKg":"4.20", ... }
  │
  ├─ 3. OnnxDppService.ClassifyDocument()
  │      ONNX model (+ keyword fallback) → "CONFIDENTIAL" | "NON_CONFIDENTIAL"
  │      Saves DppDocument record → MongoDB
  │
  ├─ 4. DppDocumentProcessingService.ProcessFields()   ← Contributions 1 & 2
  │      Per field:
  │        a. FieldConfidentialityService.Classify(fieldName, value)
  │        b. CONFIDENTIAL → FieldEncryptionService.Encrypt()   [AES-256-CBC, CSPRNG IV]
  │                        → BlindIndexService.Compute()         [HMAC-SHA256]
  │        c. NON-CONFIDENTIAL → plain value, IV="", blindIndex=null
  │      Bulk-saves List<ExtractedField> → MongoDB
  │
  └─ 5. Returns { dppId, fieldsExtracted, fields[], classification{} }
         ← confidential field values are null; public values visible
```

### Step B — `POST /api/dpp/{dppId}/generate-passport`  *(Contribution 3)*

```
DppService.GenerateDpp(dppId)
  │
  ├─ Fetch all ExtractedField records for dppId
  ├─ Filter: publicFields = WHERE IsConfidential == false
  │    pricePerKg, totalAmount, bankAccount ← never included
  │
  ├─ Build DigitalProductPassport:
  │    RubberGrade       ← publicFields["rubberGrade"]
  │    Quantity          ← publicFields["quantity"]
  │    DispatchDetails   ← publicFields["dispatchPort"]
  │    ConfidentialDataExists ← true/false (boolean only — no values)
  │    LifecycleState    = "GENERATED"
  │    DppHash           ← SHA-256(camelCaseJson, DppHash="")
  │
  ├─ Persist DigitalProductPassport → MongoDB
  └─ Returns DigitalProductPassport
```

### Step C — Controlled Access (Exporter)

```
ExporterScannerScreen → requestConfidentialAccess(lotId) → AccessRequest (PENDING)
BuyerDashboardScreen  → PendingRequestsScreen → approveAccessRequest(requestId)
ConfidentialAccessScreen → getConfidentialFields(lotId)
  └─ ConfidentialAccessService.DecryptFields()   [only after approval]
       ← plaintext exists in RAM for HTTP response only; never re-persisted
```

### Why two upload steps?

| Reason | Detail |
|--------|--------|
| Immediate feedback | Buyer sees field-level classification before committing to passport |
| Independent lifecycle | Passport can be regenerated after state changes without re-upload |
| Separation of concerns | Ingestion and issuance have different authorization requirements |
| Partial failure safety | Encrypted fields are already safe even if passport generation fails |

---

## 7. Security Properties

| Property | Mechanism | Implementation |
|----------|-----------|----------------|
| Field-level encryption | AES-256-CBC | `FieldEncryptionService` |
| Unique IV per field | CSPRNG `RandomNumberGenerator.GetBytes(16)` | `FieldEncryptionService.Encrypt()` |
| No plaintext in DB | Process-then-discard pattern | `DppDocumentProcessingService.ProcessFields()` |
| Key management | Env-var-first priority chain | `EncryptionKeyProvider` (Infrastructure/Security) |
| Key rotation support | `v{n}:` version prefix on ciphertext | `FieldEncryptionService`, `ExtractedField.KeyVersion` |
| Searchability without decryption | HMAC-SHA256 blind index (field-scoped, separate key) | `BlindIndexService` |
| DPP integrity | SHA-256 tamper seal | `DppService.ComputeSha256()` |
| Controlled decryption | Approval-gated access workflow | `ConfidentialAccessService` |
| Document-level classification | ONNX model + keyword heuristics fallback | `OnnxDppService` |
| File-level encryption | AES-256-CBC, IV prepended; RSA-wrapped key | `DppEncryptionService`, `OnnxDppService.ProcessAndSecureInvoiceAsync()` |
| Confidential messaging | AES-256 message encryption | `MessageService` |
| JWT authorization | Role-based: Buyer, Exporter, Admin | ASP.NET Core `[Authorize(Roles = "...")]` |

---

## 8. Research Distinctions vs Prior Art

| Approach | Traditional systems | This system |
|----------|--------------------|-|
| Encryption granularity | Whole document or file | Individual key-value field |
| Classification unit | Document | Field name + value |
| DPP content | Copy of document (may include financial data) | Derived artifact — public fields only |
| Financial data in DPP | Often present | Excluded by `Where(!IsConfidential)` |
| Search on encrypted data | Not supported | HMAC blind index — equality without decryption |
| IV reuse risk | Common in naïve AES-CBC | Eliminated — CSPRNG IV per field |
| Key rotation | Manual bulk re-encrypt | Version-tagged ciphertext (`v1:…`) |
| Messaging | Plaintext | Optional AES-256 encrypted messages per lot |
| Access model | All-or-nothing file share | Buyer-approval-gated per-lot field decryption |

This architecture introduces the concept of a **privacy-preserving DPP abstraction layer**: a passport that is verifiably complete, regulatory-transparent, yet structurally incapable of leaking commercial secrets.

---

## 9. Environment & Configuration

### Backend (`appsettings.json` / environment variables)

| Setting | Env var | Purpose |
|---------|---------|---------|
| `GoogleApiKey` | `GOOGLE_API_KEY` | Gemini Vision OCR |
| `DppFieldEncryptionKey` | `DPP_FIELD_ENCRYPTION_KEY` | AES-256 key for field encryption (32-byte Base64) |
| `DppBlindIndexHmacKey` | `DPP_BLIND_INDEX_HMAC_KEY` | HMAC key for blind index (separate from encryption key) |
| MongoDB connection string | `MONGODB_CONNECTION_STRING` | MongoDB Atlas or local |

### Frontend (`rubber-intelligence-app`)

The `apiClient` base URL is configured in `src/core/api/apiClient.ts`. The mobile app communicates with the backend over the local network; the IP is set in `app.json` / `apiClient.ts`. All DPP API calls are authenticated via the JWT stored in `authSlice` (Redux).

### Running locally

**Backend:**
```powershell
cd RubberIntelligence.Backend/RubberIntelligence.API
dotnet run --launch-profile http
# API available at http://localhost:5001
```

**Frontend:**
```powershell
cd rubber-intelligence-app
npx expo start -c
```

---

_Last updated: March 2026. Branch: `MithunUIdpp`._











