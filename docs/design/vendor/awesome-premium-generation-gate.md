# Premium Generation Gate Rubric (For All Image/Video/Visual Gens)

Score 0-100. Must pass to ship (target 95+ after loop).

**Taste (40 points)**:
- Specificity & hierarchy: Clear idea, real context/assets (20)
- No slop: No generic gradients/orbs, placeholder, low-contrast, untested effects (10)
- Brand fidelity: Exact tokens, references used (10)

**Tech (30 points)**:
- Tokens exact from DESIGN.md (10)
- Motion (if any): GSAP best practices or equivalent premium (10)
- Performance/Accessibility: Reduced motion, contrast, scalable (10)

**Process (20 points)**:
- Full loop followed (research refs cited, critic applied, iterated) (15)
- Evidence: Trace + inspection (5)

**Output (10 points)**:
- Inspectable artifact, ready for use (5)
- Matches 2026 premium (cinematic where appropriate, modern) (5)

**Critical Fails (Auto 0 or restart)**:
- No actual artifact inspection.
- Logo untested at small sizes or b/w.
- Motion without purpose.
- Raw gen without loop context.

Use with visual-qa-gate and OUTCOMES.md. Part of every gen loop.