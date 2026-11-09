# Immersive Planting A2 Iteration 1

## Source and version

- Baseline: `Assets/__My Project/plant -assesment1.unity`
- Iteration 1: `Assets/__My Project/Immersive Planting A2 Iteration 1.unity`
- The A1 scene remains unchanged. The WebGL build setting now points to the A2 scene.

## Changes

1. Rewrote the instruction panel as a concise, grammatical three-step task sequence.
2. Numbered each stage prompt so the user can identify the current step.
3. Changed the incorrect-choice message from “wrong / please reselect” to a neutral hint that points the user back to the instruction and gives a next action.
4. Reworded soil and watering success messages to name the completed action.

## Reason for the changes

The A1 marker confirmed the prototype ran, but asked for a stronger explanation of the design and clearer reasoning. These changes address the clarity of instructions and feedback, while preserving the existing interaction flow. They are an implementation response to the assessment feedback, not evidence that users have validated the changes.

## Validation still required

- Open this scene in the target Unity version and confirm the text fits the in-world panels.
- Run a baseline and iteration test with consenting users; capture completion by step, time, errors, retries, help requests and short feedback responses.
- Publish a separate WebGL build for this scene and add its link to the A2 report only after confirming it runs.
- Use test findings to decide whether to add selection cues, a more explicit soil-quantity display, or instruction replay/reset controls.
- Update the asset credits for every external model, texture, audio clip, image and code component. Existing project assets have not been replaced in this iteration.
