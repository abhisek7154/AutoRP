# Discord RPC dependency

This is the upstream `Lachee/discord-rpc-csharp` source pinned at commit `933f22ff9b2417be04a7c71eca2de66ebd6d4ef7` (2025-12-08). The upstream `DiscordRichPresence` 1.6.1.70 package used by AutoRP did not expose the outgoing activity `name` field. This source revision adds the supported `BaseRichPresence.Name` JSON property while retaining the same Discord RPC IPC client and presence payload implementation.

The library's MIT license is included in `LICENSE`. AutoRP builds only its `netstandard2.0` target and keeps the upstream source in this dependency folder so restore and builds do not depend on a release artifact that has not been published to NuGet.
