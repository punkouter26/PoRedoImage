# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Three confirmed audiences, all arriving with a photo already in hand:

- **Creative** — a social-media creator who needs several distinct art variations of one photo for posts. Primary flow: Bulk Generate across 10 style slots.
- **Casual** — a personal user who wants something fun out of a photo they just took. Primary flow: Meme Generation or Rap Roast.
- **Developer** — an API consumer doing integration testing and diagnostics. Primary flow: `/diag`, `/scalar/v1`, `/health`.

The operating posture is mobile-first and portrait-primary: the phone viewport is the primary
target and desktop is the adaptation, not the reverse. Breaking the phone layout is the confirmed
worst outcome of any design change.

## Product Purpose

PoRedoImage turns an ordinary photo into artistic re-renderings, memes, captioned images, style
studies, and performed roast tracks, by chaining vision, language, and image-generation models
behind a single upload. The user promise is: upload a photo, choose a transformation, get a
gallery-worthy result without writing a prompt.

Success is a user who uploads once and runs several transformations off that one image without
re-uploading or learning prompt syntax.

## Positioning

The differentiator is **one intake, many transformations, no prompt engineering**. A single uploaded
photo is held in session and can be sent through any of five transformations, including a
four-agent Style Director that writes the art prompt on the user's behalf and a Rap Roast that
produces both lyrics and a performed audio track. Per-capability model selection (remote, Ollama,
or in-browser WebGPU) is exposed to the user rather than hidden, so the cost and execution location
of each step is visible.

## Operating Context

- Entry is the Studio at `/`, which holds the uploaded image in `ImageSessionService` and offers
  the transformations as cards. Intake accepts file picker, clipboard paste, and drop-anywhere.
- Five transformation surfaces: `/image-regeneration`, `/meme-generation`, `/bulk-generate`,
  `/rap-roast`, `/style-director`. Support surfaces: `/login`, `/diag`, `/not-found`, `/Error`.
- Results panels offer "send this result to another feature", so output re-enters intake.
- "Surprise me" picks a random single-output transformation and auto-runs it. Bulk Generate is
  deliberately excluded from that pool because ten generations on a dice roll is a cost surprise.
- Long-running AI work is normal: image regeneration targets under 10s p95, bulk generate under
  45s p95 for ten variations. Waiting, partial results, and per-slot streaming are core states,
  not edge cases.
- Generation costs real money per image and the UI shows indicative per-image pricing plus a
  running session estimate.

## Capabilities and Constraints

Confirmed capabilities: image analysis (Computer Vision + GPT enhancement to tags and confidence),
image regeneration, meme captioning with server-side text overlay, bulk generation of up to 10
variations streamed live per slot, a four-agent style director, photo-to-roast-track, a persistent
per-user gallery, and per-capability AI provider selection including in-browser WebGPU inference
for image analysis.

Technical constraints that bind design work:

- .NET 10 Blazor WebAssembly, global `InteractiveWebAssembly`, **no prerender**. All interactive UI
  lives in `src/PoRedoImage.Client`. There is one `wwwroot`, in the Client project.
- Radzen Blazor and Bootstrap remain referenced and functional. Their visual layer may be
  overridden aggressively from `app.css`; packages and component usage are not to be removed.
- `TreatWarningsAsErrors` and `Nullable` are enabled solution-wide.
- Auth is BFF-shaped: the browser never holds tokens, only a claims-only authentication state.
  Production requires Microsoft Entra OIDC; development additionally offers GUEST mode.
- Mock mode (`Mocks:UseMockAi=true`) must remain visibly signalled in the UI.
- Zero-Waste policy: unused files and dead code are deleted rather than left in place.

Undecided / not established: no confirmed launch date, pricing model for end users, or
internationalisation requirement.

## Brand Commitments

The product name **PoRedoImage** is binding and appears in page titles.

Nothing else is binding. The incumbent navy/plum palette, the existing UX copy, and the emoji
feature glyphs were all explicitly released by the user for replacement.

## Evidence on Hand

Real: the deployed instance at `https://poredoimage-web.azurewebsites.net`, a `/health` endpoint,
`/scalar/v1` API docs, architecture diagrams under `docs/`, and the working transformation
pipelines themselves. The product's own output — generated images, memes, roast tracks — is the
demonstrable evidence and is producible on demand.

Absent and not to be fabricated: user counts, testimonials, customer names, press, benchmarks
beyond the latency targets recorded above, and any end-user pricing or licensing claim.

## Product Principles

1. **One intake, many outcomes.** The uploaded photo is the durable object of the session; every
   surface is a verb applied to it. Never make the user re-upload to change their mind.
2. **The wait is part of the product.** AI work takes seconds to a minute. Progress, partial
   results, and per-slot state are first-class, not loading spinners bolted on.
3. **Cost and execution location stay visible.** Which model ran, where it ran, and roughly what it
   cost are the user's information, never hidden to make the flow look smoother.
4. **No prompt engineering required.** The product's job is to remove prompt authorship from the
   user. Exposed prompt controls are an optional depth, never the price of entry.
5. **The phone is the real device.** Portrait is the primary composition. A layout that only
   resolves on a wide viewport has failed.

## Accessibility & Inclusion

No externally mandated standard was established. The incumbent codebase records deliberate WCAG AA
contrast reasoning in its token comments (which token is safe on which ground), and that intent is
to be preserved: text contrast ratios are checked against their actual background, and interactive
controls keep visible focus states.
