# Unity.CUID

A lightweight (and faster) non-cryptographic version of CUID 2 with no dependencies tailored for Unity.

This is an id that should be collision resistant (offline) and can be used by your team to identify unique objects without fearing that the same id will be used when merging.

## Installation

Add the dependency to your `manifest.json`

```json
{
  "dependencies": {
    "jd.boiv.in.cuid": "https://github.com/starburst997/Unity.CUID.git"
  }
}
```

## Usage

Simply call the following to get an id:

```csharp
var id = Cuid2.Get();
Debug.Log($"My CUID: {id}");
```

## TODO

- Better readme
- Zero Alloc version?

## Credits

Based on [cuid.net](https://github.com/visus-io/cuid.net) which is based on the og [cuid2](https://github.com/paralleldrive/cuid2).

Include a slim version of SHA3 from [BouncyCastle](https://github.com/bcgit/bc-csharp).