---
version: "0.1.1"
level: copilot
processes:
  design: copilot
  implementation: copilot
  testing: assist
  documentation: copilot
  review: assist
  deployment: copilot
---

## Notes

- Claude (by Anthropic) wrote nearly all of the code, documentation and release steps in this repository. The maintainer is not a professional programmer. He decides what the plugin should do and how it should behave, and writes the feature specifications.
- Every change is tested by the maintainer in the game, in real content, and reviewed before it goes public. Claude asks before it builds, commits, pushes, publishes a release or edits the plugin manifest in the hub repository.
- Testing is marked `assist` because Claude reads logs and writes small probe programs, while the pass or fail judgment comes from playing the game. Review is marked `assist` because the maintainer reviews behavior and wording rather than line by line code.
