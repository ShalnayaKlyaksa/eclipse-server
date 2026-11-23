# Advanced medicine

Humanoids with `AdvancedHealthComponent` use advanced physiology as the health authority.
`Damageable` remains the common incoming-damage interface for weapons, atmos and reagents;
its accumulated total is not an additional humanoid health pool. Other damageable entities
(structures, machines and mobs without advanced anatomy) retain their existing behaviour.

## Body function

`BodyPartState.TissueFraction` is the fraction of preserved anatomical tissue. Tissue maxima
of zero mean the tissue is absent. The chip displays this separately from function.

`SharedMedicalConditionSystem.GetFunction` combines viable muscles, nerves, vessels, bones
and core organs. Fractures reduce function; splints partly restore function without repairing
the bone. A destroyed part has zero function. A limb chain uses its weakest segment, avoiding
three applications of the same debuff for shoulder, forearm and hand.

Leg function scales walking and sprinting. Two failed legs leave slow movement, with no
sprinting. Hand function scales melee damage and firearm spread. A failed hand blocks
insertion into its hand container, use of held items, firing and two-handed wielding.

## Extending conditions

Add a prototype under `Resources/Prototypes/_Eclipse/AdvancedHealth/`, for example:

```yaml
- type: medicalCondition
  id: ExampleNerveBlock
  name: example-nerve-block-name
  description: example-nerve-block-description
  beneficial: false
  functionMultiplier: 0
```

Add both localisation keys in the English and Russian locale trees. Apply or remove the
condition on the server using:

```cs
var conditions = EntityManager.System<SharedMedicalConditionSystem>();
conditions.AddCondition((patient, health), BodyPartSlot.LeftForearm, "ExampleNerveBlock");
conditions.RemoveCondition((patient, health), BodyPartSlot.LeftForearm, "ExampleNerveBlock");
```

The same prototype is upserted rather than duplicated. Severity is clamped to 0..1 and blends
the multiplier from neutral (1) to its configured value. `beneficial` affects presentation;
it does not fabricate healing. Wounds, dressings, splints and tourniquets are displayed from
their original data, not copied into additional condition objects.

The initial extension contract supports anatomy, text and function modifiers. Timed expiry,
custom drug actions and surgery stages require their own gameplay systems; the chip does
not pretend these mechanics exist. Surgery and character attributes are separate work.

## Legacy integration

- Damage totals no longer decide advanced patients' critical/dead states or movement penalties.
- The legacy blood-regulation/bloodloss-damage loop is skipped. Blood solutions remain for
  chemistry and forensics and are synchronised from advanced blood volume.
- Respirators still perform gas exchange; advanced oxygenation owns the physiological
  consequences, without a second accumulation of suffocation HP damage.
- Reagent health effects use advanced soft-tissue repair and toxin/radiation loads.
  Unprotected penetrating wounds and embedded foreign bodies must be dealt with first.
  Ordinary repair does not restore bones, nerves or destroyed parts.
- Blood-restoring reagent effects modify advanced volume. Advanced transfusion compatibility
  and oxygen-carrying capacity remain handled by the existing transfusion system.
- Ordinary topical HP healing is already disabled for advanced patients. Treatment actions
  use the existing advanced procedures and server-side validation.

Ventilation shown by the chip is a modelled value: respiratory rate × tidal volume ×
airway/lung capacity, in L/min. It is not a gas-flow measurement from atmos. Blood-loss
readouts use the same effective rate as physiology, including the tourniquet multiplier.

The UI refreshes scalar values twice per second and rebuilds condition cards only when
their text changes. Client effects do not schedule server work.


## Chip presentation

The chip uses black and amber panels over a translucent world view. The left diagnostics
scroll independently of the labelled toolbelt on short viewports. The held-item preview is
an input-transparent SpriteView in the chip overlay; inventory actions still use the normal
hands/inventory systems. The original authored thermometer is retained. Horizontal gauges use fixed-width vector edges and calibrated ticks, so changing width does not squash their end caps. Brain integrity and temperature are the large anchor instruments; consciousness and pain are horizontal meters between them. Shock, perfusion, infection and toxins use compact status indicators; explanatory text is available on hover. Hands show active-tool cards, while storage uses a separate quick-access strip with empty-slot silhouettes.

Abdomen and pelvis share one medical-map region. Their conditions remain separate and the
procedure selector targets the original BodyPartSlot; grouping never transfers wounds or
combines server anatomy. The overview uses the worse regional state. The general view shows injuries and treatment, not generic bone/organ integrity cards.
Neck injuries share the head map region; a damaged neck remains a selectable procedure
subsection. Individual organ identity, organ-local infection, crack subtypes and transplant
operations are not yet modelled.

Zone equipment lists relevant worn items and their display classes. BodyArmorRating is a
cosmetic classification, so it is not presented as a damage-absorption percentage. The rhythm
strip visualizes simulated heart rate; its signal is sampled per time step instead of being
held constant across all columns crossed during a render frame.


## Exertion and corpse feedback

The chip displays endurance and sleep debt in place of the former infection/toxin meters.
Open wounds retain contamination risk but no longer create a fabricated systemic infection
load. The future diagnostic minigame must establish confirmed disease information separately.
The pulse drifts smoothly within +/-5 of the resting baseline, in addition to real physiological
compensation and exertion. Brain, immune-defense and bleeding icons use Lucide assets;
the ISC license and original SVG sources are retained alongside the PNGs.

Actual MobState.Dead takes precedence over late vital updates in the damage overlay and
chip. Corpses do not resume automatic consciousness or heartbeat recovery; an explicit
revival can change the authoritative state. Advanced critical/dead feedback uses a bounded,
non-flashing dark vignette instead of the old pulsing white mask.

See advanced-exertion-design.md for the implemented work/endurance contract and
advanced-medical-layers.md for future diagnostic layers.
