# AI Disclosure

Basis is developed with help from AI tools, and we'd rather say so plainly than have anyone guess. This document covers how AI is used to build Basis, what actually ships in the product, and what we ask of contributors who use AI tools themselves.

## How Basis is developed

AI coding assistants (LLM-based tools such as Claude Code) are used in Basis development for code only: implementation, refactoring, tests, debugging, and developer docs. AI is not used for creative content: the art, imagery, audio, and 3D assets in Basis are made by humans.

As a concrete example, AI wrote many of the automated tests in Basis. That work would have taken a human months, and it means far more of the codebase is covered by tests than would otherwise be possible.

Direction, review, and responsibility stay human. Changes land because a maintainer decided they should, reviewed them, and stands behind them, and the quality bar is the same for every change regardless of how it was written. We don't tag individual commits or files as AI-assisted; this document is the project-level disclosure instead.

## What ships in Basis

**Basis does not embed generative AI.** The client and server do not call LLM or other generative AI services, and Basis does not send user data (voice, motion, text, or anything else) to AI services or collect it to train AI models.

That's a commitment about the future, not just a description of today: **we will never include generative image or video AI in Basis.**

Basis does ship small machine-learning models for classic signal-processing jobs, running entirely on your device:

- **RNNoise**: neural noise suppression on your own microphone input.
- **OpenLipSync**: lip-sync viseme inference from your own voice, via ONNX Runtime.
- **MediaPipe**: optional webcam-based tracking, only if you enable it.

These run locally and upload nothing.

## AI-assisted contributions

AI-assisted contributions are welcome, and are held to the same bar as everything else in [CONTRIBUTING.md](./CONTRIBUTING.md):

- **You are the author.** Understand what you submit well enough to explain and defend it in review. "The AI wrote it" is not an answer to a review question.
- **Review your own output before asking us to.** Don't open a PR whose contents you haven't read yourself; unreviewed AI output is easy to spot and wastes reviewer time.
- **You own the licensing.** Treat AI output like code you found somewhere: make sure it isn't reproducing someone else's incompatibly-licensed work. Everything you contribute lands under MIT.
- **Keep reports real.** Using AI to write up a bug report or feature request is fine, but logs, reproduction steps, and observed behaviour must be genuine, never invented or "reconstructed".
- **Code only applies to contributions too.** Don't submit AI-generated art, images, video, audio, or 3D assets; the commitment above covers the whole project, not just what the maintainers ship.

If a substantial part of a PR is AI-generated, a brief mention under **Notes** in the PR template is appreciated. It's not required, and it won't count against you.

TL;DR:
* Basis development uses AI for code only; humans direct, review, and own every change. Creative assets are human-made.
* No generative AI in the product, and generative image or video AI will never be added. No user data goes to AI services. Local on-device ML only (noise suppression, lip sync, optional webcam tracking).
* AI-assisted PRs are welcome: understand what you submit, own the correctness and licensing, and don't hand us unreviewed output.
