// Elektron mascot controller — drives the cut-out Lottie by chat state.
//
// Usage:
//   import { Mascot } from "./mascot";
//   const mascot = new Mascot(containerEl);
//   await mascot.ready;
//   mascot.set("thinking");   // while retrieving / waiting for the API
//   mascot.set("talking");    // while the answer streams
//   mascot.set("idle");       // back to resting
//   mascot.set("notfound");   // no answer — plays once, then returns to idle
//
// Requires lottie-web (npm i lottie-web). The mascot JSON is self-contained
// (figure + eyelids embedded), so no asset-path config is needed on the host.

import lottie, { AnimationItem } from "lottie-web";
import animationData from "../assets/elektron.json";

export type MascotState = "idle" | "thinking" | "talking" | "notfound";

type Segment = [number, number];

const FALLBACK_SEGMENTS: Record<MascotState, Segment> = {
  idle: [0, 90],
  thinking: [90, 195],
  talking: [195, 285],
  notfound: [285, 360],
};

export class Mascot {
  readonly ready: Promise<void>;
  private anim: AnimationItem;
  private segments: Record<MascotState, Segment>;
  private state: MascotState = "idle";

  constructor(container: HTMLElement) {
    // segments come from the JSON's meta so the build stays the single source of truth
    const meta = (animationData as any)?.meta?.segments as
      | Record<MascotState, Segment>
      | undefined;
    this.segments = meta ?? FALLBACK_SEGMENTS;

    this.anim = lottie.loadAnimation({
      container,
      renderer: "svg",
      loop: true,
      autoplay: false,
      animationData: animationData as any,
    });

    this.ready = new Promise<void>((resolve) => {
      this.anim.addEventListener("DOMLoaded", () => {
        this.play("idle");
        resolve();
      });
    });

    // notfound plays once, then settles back to idle
    this.anim.addEventListener("complete", () => {
      if (this.state === "notfound") this.play("idle");
    });
  }

  /** Switch state. Idempotent — re-setting the current state is a no-op. */
  set(state: MascotState): void {
    if (state === this.state) return;
    this.play(state);
  }

  current(): MascotState {
    return this.state;
  }

  destroy(): void {
    this.anim.destroy();
  }

  private play(state: MascotState): void {
    this.state = state;
    this.anim.loop = state !== "notfound";
    // NOTE: after playSegments, frame addressing is segment-relative — only
    // ever drive the mascot through set()/play(), never goToAndStop(globalFrame).
    this.anim.playSegments(this.segments[state], true);
  }
}
