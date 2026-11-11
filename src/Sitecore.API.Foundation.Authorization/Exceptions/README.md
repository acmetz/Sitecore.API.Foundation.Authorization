# Exceptions

## Overview

Exception hierarchy thrown by `SitecoreTokenService` in `../Services/`.
Callers can catch `SitecoreAuthException` to handle every library-specific
authentication failure, or catch a subclass for HTTP or response-parsing
failures.

## Interface

All types are in `[SitecoreAuthException.cs]`.

```text
System.Exception
└── SitecoreAuthException
    ├── SitecoreAuthHttpException
    └── SitecoreAuthResponseException
```

- `SitecoreAuthException : Exception` — `[SitecoreAuthException.cs:SitecoreAuthException]`
  - `SitecoreAuthException(string message)`
  - `SitecoreAuthException(string message, Exception innerException)`
- `SitecoreAuthHttpException : SitecoreAuthException` — `[SitecoreAuthException.cs:SitecoreAuthHttpException]`
  - `int StatusCode { get; }`
  - `string? RequestUrl { get; }`
  - `SitecoreAuthHttpException(int statusCode, string? requestUrl, string message)`
  - `SitecoreAuthHttpException(int statusCode, string? requestUrl, string message, Exception innerException)`
- `SitecoreAuthResponseException : SitecoreAuthException` — `[SitecoreAuthException.cs:SitecoreAuthResponseException]`
  - `SitecoreAuthResponseException(string message)`
  - `SitecoreAuthResponseException(string message, Exception innerException)`

## Invariants

- Every library-specific exception derives from `SitecoreAuthException`
  `[SitecoreAuthException.cs:SitecoreAuthHttpException]`,
  `[SitecoreAuthException.cs:SitecoreAuthResponseException]`.
- `StatusCode` and `RequestUrl` are set only by the constructor and are
  read-only `[SitecoreAuthException.cs:SitecoreAuthHttpException]`.
- Throw sites in `SitecoreTokenService` (for reference):
  - `SitecoreAuthException` — invalid or insecure `AuthTokenUrl`.
  - `SitecoreAuthHttpException` — non-success HTTP status; `RequestUrl` is the
    configured `AuthTokenUrl`.
  - `SitecoreAuthResponseException` — JSON parse failure, missing or empty
    `access_token`, or `expires_in <= 0`.
- The types do not override `Message`, `ToString()`, or add serialization
  constructors.

## Side Effects

- None.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~SitecoreAuthExceptionTests|FullyQualifiedName~SitecoreAuthHttpExceptionTests|FullyQualifiedName~SitecoreAuthResponseExceptionTests"` (from the repository root)
