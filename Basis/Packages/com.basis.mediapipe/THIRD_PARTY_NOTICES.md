# Third-Party Notices

This package integrates with, but does not itself redistribute, the following third-party
software. Installing the optional dependency below brings their licenses into your build.

## MediaPipe Unity Plugin (homuler)

- Project: https://github.com/homuler/MediaPipeUnityPlugin
- License: Apache License 2.0
- Optional dependency (`com.github.homuler.mediapipe`). When installed, this package's
  `BasisMediaPipe.Homuler` assembly uses it for landmark inference. The plugin bundles native
  MediaPipe libraries and model assets under its own license terms.

## MediaPipe (Google)

- Project: https://github.com/google-ai-edge/mediapipe
- License: Apache License 2.0
- The underlying framework and the pretrained landmark/blendshape models (FaceLandmarker,
  HandLandmarker, PoseLandmarker) are provided by Google under Apache 2.0. Individual model
  cards may carry additional usage terms; review them before redistribution.

A copy of the Apache License 2.0 is available at https://www.apache.org/licenses/LICENSE-2.0.
