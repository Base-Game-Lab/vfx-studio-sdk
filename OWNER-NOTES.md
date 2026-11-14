# Notes for this repo's Owner

This file is for the studio's own agents. Customers read `README.md`; nothing here is about using the package.

This repo is public. Before it was made public, on 5 November 2026, its history was checked for customer names, money and private
paths. Keep all three, and customer files, out of every file, issue and pull request here.

## How this repo is made

The package's source is `unity/com.basegamelab.vfx` in vfx-studio, a private repo. Its `tools/unity/publish.py` copies the source
here, commits it as `com.basegamelab.vfx <version>` ("From vfx-studio <commit>") and tags `v<version>`. It refuses a tag that
already exists, and it refuses a package file that contains certain words, customer names among them (`FORBIDDEN` in
`tools/unity/pack.py`).

- Change the package in vfx-studio, never here. A publish first removes every file in this repo and then copies the source in, so
  a hand edit to `README.md`, `Documentation~`, `LICENSE.md`, `package.json` or `Editor/` is lost at the next publish.
- The same publish also removes `OWNER.md`, `LOGBOOK.md`, this file and their `.meta` files, because they are not in the source.
  vfx-studio issue 26 asks for that to be fixed before the next publish.
- On 14 November 2026 the source was at 0.4.0, which is not published: the newest tag here is v0.3.9. 0.4.0 adds `VfxTrigger`, a
  component that ships in the game, so from 0.4.0 the package is no longer editor only. Its license still says it is: vfx-studio
  issue 27, waiting on Batu.

## Who installs it

`OWNER.md` says to find the customers by looking, each time it matters. So this is a dated finding, not a list to trust. On 14
November 2026:

- **No studio game installs it.** None of the eight `Packages/manifest.json` files on the default branches of the Base-Game-Lab
  repos lists `com.basegamelab.vfx`: seven game repos and bgl-kit's starter template.
- **One test install, from this repo, by version tag.** A Unity project on Batu's Windows PC holds a customer's exported scene. On
  5 November 2026 Batu installed the package there from this repo at v0.3.0, the way that customer would, to find what trips a new
  user. Tags v0.3.1 to v0.3.9 were all released that day to fix what he hit, and the project now pins `#v0.3.9`. vfx-studio's
  friction log of that day lists each snag and the version that fixed it: `docs/reports/2026-11-05-bgl250-roleplay/friction.md`.
- **Other test projects do not use this repo.** A second project on that PC, and vfx-studio's own Unity test projects, load the
  package by a `file:` path from a vfx-studio working copy.
- **No customer has it yet.** vfx-studio's records show no customer has been sent the package. Whether and when one gets it is
  Batu's decision.

To look again, read each game repo's `Packages/manifest.json`, and search the Unity projects on the studio's machines for
`vfx-studio-sdk.git`.
