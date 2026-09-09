# Installer images

- `icon.png`: the supplied Beastmaster artwork, padded to a square and resized to 256 × 256.
- `screenshots/uncaptured-overview.png`: the supplied in-game Sandstone Golem screenshot, resized to fit the installer.
- `screenshots/uncaptured-detail.png`: a close-up crop of the same screenshot highlighting the uncaptured marker.
- `source/`: the unmodified source images supplied by the project owner.

Run `tools/prepare-images.ps1` on Windows to reproduce the crops and resizing. No game labels or textures are redrawn. Icon size is at most 512 × 512; previews fit within 730 × 380, as required by Dalamud's installer. FFXIV artwork and game imagery belong to their respective owners.
