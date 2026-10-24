advanced-health-part-auto = Auto

advanced-health-chip-patient = MEDICAL CHIP · {$name}
advanced-health-chip-body = BODY MAP
advanced-health-chip-body-hint = Select a body part. Green fill: selection · red: injury · cross: fracture · cyan marker: treatment.
advanced-health-chip-function = FUNCTION
advanced-health-chip-tissues = TISSUE CONDITION
advanced-health-chip-conditions = CONDITIONS AND TREATMENTS
advanced-health-chip-stable = Condition stable
advanced-health-chip-unstable = Vital functions impaired
advanced-health-chip-arrest = ALERT · CARDIAC ARREST
advanced-health-chip-unconscious = Unconscious
advanced-health-chip-suffocating = Respiratory gas supply impaired
advanced-health-chip-system-details =
    Shock: {$shock}%
    Perfusion: {$perfusion}%
    Infection load: {$infection}%
    Toxin load: {$toxins}%
advanced-health-chip-radiation-load = {$value}%
advanced-health-chip-breathing =
    Oxygen supply: {$oxygen}%.
    Respiration: {$rate} breaths/min.
    Below: modelled ventilation in litres per minute, adjusted for airway and lung condition.
advanced-health-chip-no-conditions = No injuries or additional conditions detected in this body part.
advanced-health-chip-wound = {$type} ×{$count} · {$severity}
advanced-health-chip-wound-detail = Pain load: {$pain}. {$dirty}
advanced-health-chip-contaminated = Wound contamination present.
advanced-health-chip-no-contamination = No contamination recorded.
advanced-health-chip-bleeding-detail = Blood loss: {$total} L/min, including {$internal} L/min internally. Dressings and tourniquets are accounted for.
advanced-health-chip-foreign-detail = A foreign body remains embedded. Dressing the wound does not remove it.
advanced-health-chip-bandage-detail = External bleeding has been treated. A dressing does not restore damaged tissue.
advanced-health-chip-suture-detail = Wound sutured. Internal injuries require separate treatment.
advanced-health-chip-splint-detail = Fracture stabilised. Function partly restored; the bone remains damaged.
advanced-health-chip-tourniquet-detail = External blood loss restricted. Internal bleeding and tissue damage remain.
advanced-health-chip-destroyed-detail = This body part cannot function. An ordinary dressing cannot restore it.
advanced-health-chip-function-tooltip = Ability to use the body part, based on muscles, nerves, vessels, bones, organs and additional conditions. Connected segments limit the entire limb.
advanced-health-chip-tissue-tooltip =
    Skin: {$skin}% · Muscle: {$muscle}%
    Bone: {$bone} · Vessels: {$vessels}%
    Nerves: {$nerves}% · Organs: {$organs}
    A dash means that tissue type is absent from this anatomy.
advanced-health-chip-function-failed = Function lost. The affected hand is unavailable; leg failure prevents sprinting.
advanced-health-chip-function-impaired = Function lost: {$penalty}%. Restrictions scale with impairment and connected limb segments.
advanced-health-chip-function-normal = Function preserved. Hover the tissue bar for its detailed composition.
advanced-health-chip-needs-tool = Hold a suitable tool or treatment material.
advanced-health-chip-treatment-hint = Begin treatment on the selected body part. The server validates completion and material consumption.
advanced-health-part-head = Head
advanced-health-part-neck = Neck
advanced-health-part-chest = Chest
advanced-health-part-abdomen = Abdomen
advanced-health-part-pelvis = Pelvis
advanced-health-part-leftupperarm = left upper arm
advanced-health-part-leftforearm = left forearm
advanced-health-part-lefthand = left hand
advanced-health-part-rightupperarm = right upper arm
advanced-health-part-rightforearm = right forearm
advanced-health-part-righthand = right hand
advanced-health-part-leftthigh = left thigh
advanced-health-part-leftshin = left shin
advanced-health-part-leftfoot = left foot
advanced-health-part-rightthigh = right thigh
advanced-health-part-rightshin = right shin
advanced-health-part-rightfoot = right foot

advanced-health-target-verb = Aim: {$part}
advanced-health-target-selected = Aiming zone: {$part}.
advanced-health-target-tooltip = Accuracy penalty: {$penalty}. {$effect}
advanced-health-target-tooltip-auto = Weighted automatic body-zone selection without an accuracy penalty.
advanced-health-target-effect-vital = A hit can rapidly incapacitate or kill.
advanced-health-target-effect-internal = Risk of organ damage and internal bleeding.
advanced-health-target-effect-arm = May impair weapon and tool use.
advanced-health-target-effect-leg = May impair movement and stability.
advanced-health-treatment-applied = Treatment applied to the {$part}.
advanced-health-treatment-tourniquet-invalid = A tourniquet can only be applied to a limb.
advanced-health-treatment-no-item = No suitable medical item in hand.
advanced-health-treatment-too-far = Too far away to provide aid.

advanced-health-scanner-title = Advanced physiology
advanced-health-scanner-blood = Blood: {$value ->
    [one] {$value}%
   *[other] {$value}%
}
advanced-health-scanner-oxygen = Oxygenation: {$value}
advanced-health-scanner-pain = Pain: {$value}
advanced-health-scanner-shock = Shock: {$value}
advanced-health-scanner-trauma = Trauma load: {$value}
advanced-health-scanner-no-bleeding = No active bleeding detected.
advanced-health-scanner-bleeding = Active bleeding: {$part}.

advanced-health-window-title = Body condition
advanced-health-window-vitals = Systemic condition
advanced-health-window-body-parts = Body parts
advanced-health-window-oxygenation = Oxygenation: {$value}%
advanced-health-window-shock = Shock: {$value}%
advanced-health-window-trauma = Trauma load: {$value}
advanced-health-window-part-row = {$part}: {$severity} · wounds: {$wounds} · {$statuses}
advanced-health-window-status-bleeding = bleeding
advanced-health-window-status-bandaged = bandaged
advanced-health-window-status-splinted = splinted
advanced-health-window-status-tourniquet = tourniquet
advanced-health-window-status-destroyed = destroyed
advanced-health-severity-normal = normal
advanced-health-severity-minor = minor
advanced-health-severity-moderate = moderate
advanced-health-severity-severe = severe
advanced-health-severity-critical = critical

ent-AdvancedBandage = field bandage
    .desc = A sterile bandage for controlling external bleeding in a selected body zone.
ent-AdvancedPressureBandage = pressure bandage
    .desc = A tight dressing effective against severe external bleeding.
ent-AdvancedTourniquet = tourniquet
    .desc = Almost completely stops external bleeding from a limb.
ent-AdvancedSplint = medical splint
    .desc = Stabilizes fractures and reduces their pain.
ent-AdvancedHemostaticPowder = hemostatic powder
    .desc = Temporarily reduces external bleeding.
ent-AdvancedSutureKit = suture kit
    .desc = Closes open wounds, stopping bleeding and reducing infection risk.
ent-AdvancedForcepsPack = surgical forceps
    .desc = Removes shrapnel and foreign bodies from a wound.
ent-BasicMedScanner = basic medical scanner
    .desc = Shows a readable summary and localized injuries for advanced-health patients.

# Full-screen self-diagnostic UI
advanced-health-ui-select-part = Select a body zone
advanced-health-ui-no-wounds = No injuries detected
advanced-health-ui-wounds-header = Condition

advanced-health-cond-bleeding = Bleeding
advanced-health-cond-foreign = Foreign body
advanced-health-cond-fracture = Fracture
advanced-health-cond-treated = Treated: {$list}
advanced-health-ui-tissue-header = Tissue integrity
advanced-health-ui-actions-header = Aid

advanced-health-vital-blood = Blood
advanced-health-vital-blood-value = {$liters} L · {$percent}%
advanced-health-vital-oxygen = Oxygen
advanced-health-vital-consciousness = Consciousness
advanced-health-vital-pain = Pain
advanced-health-vital-shock = Shock
advanced-health-vital-trauma = Trauma
advanced-health-vital-temperature = Temperature
advanced-health-vital-temperature-value = {$value}°C
advanced-health-vital-heart = Heart
advanced-health-vital-heart-beating = beating
advanced-health-vital-heart-stopped = STOPPED
advanced-health-vital-infection = Infection
advanced-health-vital-percent = {$value}%

# Fullscreen status menu
advanced-health-legend-healthy = Healthy
advanced-health-legend-minor = Minor damage
advanced-health-legend-moderate = Moderate damage
advanced-health-legend-severe = Severe damage
advanced-health-legend-critical = Critical damage
advanced-health-menu-liters = {$value} L
advanced-health-menu-liters-per-minute = {$value} L/m
advanced-health-menu-per-minute = {$value}/m
advanced-health-menu-rad = {$value}rad
advanced-health-menu-bp = {$sys}/{$dia} · {$pulse} bpm
advanced-health-menu-o2 = {$value}% O2
advanced-health-menu-wounds = Injuries
advanced-health-menu-equipment = Equipment
advanced-health-slot-hand = Hand
advanced-health-slot-belt = Belt
advanced-health-slot-back = Back
advanced-health-slot-pocket = Pocket
advanced-health-slot-suit = Suit
advanced-health-slot-id = ID
advanced-health-slot-interact-hint = {$slot}
    LMB — take / switch hand · RMB — actions · E — open · Shift+E — alternate action

advanced-health-vital-immunity = Immune defense
advanced-health-vital-brain-tooltip = Brain tissue integrity. At 50% or below normal defibrillation is impossible; mannitol is required.
advanced-health-vital-consciousness-tooltip = Consciousness level. A healthy character has 100%; hypoxia, shock, and poor cerebral perfusion reduce it.
advanced-health-vital-pain-tooltip = Pain level. It contributes to shock, unconsciousness, and difficulty performing medical procedures.
advanced-health-vital-immunity-tooltip = The body's immune defense. It suppresses infection in open and contaminated wounds.
advanced-health-vital-blood-tooltip = Current blood volume and its oxygen-carrying capacity.
advanced-health-vital-bleeding-tooltip = Total external and internal blood loss per minute.
advanced-health-vital-respiration-tooltip = Breaths per minute. Suffocation first causes rapid breathing; severe hypoxia lowers consciousness and pressure, damages the brain, and can stop the heart.
advanced-health-vital-radiation-tooltip = Accumulated radiation damage.
advanced-health-vital-thirst-tooltip = The body's fluid reserve.
advanced-health-vital-hunger-tooltip = The body's nutrient reserve.
advanced-health-vital-temperature-tooltip = Core body temperature; it changes gradually.
advanced-health-vital-heart-tooltip = Heart rhythm. The icon pulses in sync with the actual heart rate.
advanced-health-zone-protection = Protection
advanced-health-zone-bleeding = Bleeding

advanced-disease-unknown = unknown disease
advanced-disease-space-flu = space flu
advanced-disease-wound-fever = wound fever
advanced-disease-status-weakness = Weakness
advanced-disease-status-fever = Fever
advanced-disease-too-weak-twohanded = You are too weak to use a two-handed weapon.

advanced-health-blood-group-tooltip = Body fluid: {$group}. Oxygen carrying: {$carry}%
advanced-health-transfusion-ok = Compatible transfusion. Volume restored.
advanced-health-transfusion-fluid = Fluid infusion: volume restored, but it carries no oxygen.
advanced-health-transfusion-incompatible = INCOMPATIBLE BLOOD! Hemolytic reaction.
advanced-health-transfusion-incompatible-imperial = INCOMPATIBLE BLOOD! Hemolytic reaction.
advanced-health-transfusion-incompatible-molei = INCOMPATIBLE HEMOLYMPH! Clotting reaction and weakness.
advanced-health-transfusion-incompatible-dwarf = INCOMPATIBLE DENSE BLOOD! Thrombosis and pain reaction.
advanced-health-transfusion-incompatible-lavrite = INCOMPATIBLE THERMOBLOOD! Crystallization shock.
advanced-health-transfusion-incompatible-kobold = INCOMPATIBLE REPTILE BLOOD! Toxic shock.
advanced-health-transfusion-incompatible-saurian = INCOMPATIBLE REPTILE BLOOD! Temperature crash and weakness.
advanced-health-transfusion-incompatible-therian = INCOMPATIBLE BLOOD! Immune reaction and fever.
advanced-health-transfusion-incompatible-arkane = INCOMPATIBLE ICHOR! Unstable burning reaction.
advanced-health-transfusion-incompatible-avian = INCOMPATIBLE LIGHT BLOOD! Oxygen-starvation reaction.
advanced-health-transfusion-incompatible-elir = INCOMPATIBLE REFINED BLOOD! Toxic impurity reaction.
advanced-health-transfusion-incompatible-slimefolk = INCOMPATIBLE PLASMA! Plasma shock and body destabilization.

advanced-health-tissue-skin = Skin
advanced-health-tissue-muscle = Muscle
advanced-health-tissue-bone = Bone
advanced-health-tissue-vessel = Vessels
advanced-health-tissue-nerve = Nerves
advanced-health-tissue-organ = Organs

advanced-health-wound-cut = Cut
advanced-health-wound-puncture = Puncture
advanced-health-wound-gunshot = Gunshot wound
advanced-health-wound-bruise = Bruise
advanced-health-wound-burn = Burn
advanced-health-wound-fracture = Fracture
advanced-health-wound-shrapnel = Shrapnel wound
advanced-health-wound-organdamage = Organ damage
advanced-health-wound-nervedamage = Nerve damage
advanced-health-wound-row = {$type} · {$severity}
advanced-health-wound-flag-bleeding = bleeding
advanced-health-wound-flag-bandaged = bandaged
advanced-health-wound-flag-sutured = sutured
advanced-health-wound-flag-foreign = foreign body

advanced-health-action-bandage = Bandage
advanced-health-action-pressurebandage = Pressure bandage
advanced-health-action-tourniquet = Apply tourniquet
advanced-health-action-splint = Apply splint
advanced-health-action-hemostatic = Hemostatic
advanced-health-action-suture = Suture wound
advanced-health-action-foreignbodyremoval = Remove foreign body
advanced-health-action-requires = Requires in hand: {$item}
advanced-health-action-bare-hand = Bare hands allowed (harder)
advanced-health-treatment-not-needed = That procedure isn't needed here.

advanced-health-minigame-cancel = Cancel
advanced-health-minigame-success = Done!
advanced-health-minigame-fail-generic = Failed — try again.
advanced-health-minigame-fail-shake = Too much sideways movement — the wound tore wider.
advanced-health-minigame-fail-suture = Missed the stitch — thread snapped.

advanced-health-minigame-title-foreignbodyremoval = Extract shrapnel
advanced-health-minigame-title-bandage = Bandaging
advanced-health-minigame-title-pressurebandage = Pressure bandage
advanced-health-minigame-title-hemostatic = Hemostatic
advanced-health-minigame-title-suture = Suturing
advanced-health-minigame-title-splint = Splinting
advanced-health-minigame-title-tourniquet = Tourniquet

advanced-health-minigame-hint-extraction-hand = Hold LMB and pull the shards straight up — there are four. You can release and continue. Pain makes your hand shake.
advanced-health-minigame-hint-extraction-tool = Hold LMB and pull the shards up with forceps — there are four. Don't drift sideways.
advanced-health-minigame-hint-steady = Hold the cursor inside the green zone while holding LMB.
advanced-health-minigame-hint-suture = Stitch the points in order.
advanced-health-minigame-hint-splint = Align the bone fragment and release LMB.
advanced-health-minigame-hint-tourniquet = Hold LMB in the center to tighten the tourniquet.

advanced-health-minigame-status-extraction = Shard {$shard}/{$total} — {$percent}%
advanced-health-minigame-status-steady = Hold steady: {$seconds}s
advanced-health-minigame-status-tourniquet = Tightening: {$seconds}s
advanced-health-minigame-status-suture = Stitch {$step}/{$total}
advanced-health-minigame-status-splint = Align the bone and release
advanced-health-cond-foreign-count = Foreign body ×{$count}
advanced-health-minigame-hint-wrap = Hold LMB and circle the wound. Each segment spends 1% of the roll and closes 0.01 L/min. Using the whole roll is optional.
advanced-health-minigame-status-wrap = Bandage: {$percent}% · Bleeding: {$bleed} L/m
advanced-health-bandage-durability = Bandage durability: {$percent}%
advanced-health-bandage-applied = Bandage applied to { $part }. Roll left: {$percent}%.
advanced-health-aim-menu-title = Aim
advanced-health-aim-key-hint = Hold { $key }, release to confirm. Tap for auto aim.
advanced-health-aim-penalty = Accuracy penalty: {$value}
advanced-health-aim-too-fast = Too fast!

advanced-health-unconscious-shock =
    Shock overwhelms you ({$shock}% at a {$threshold}% threshold).
    Pain, blood loss, and trauma knock you out — your ears ring and sounds grow muffled.
advanced-health-unconscious-consciousness =
    Consciousness is fading ({$consciousness}%).
    Low oxygen and shock cloud your mind — you lose control.
advanced-health-unconscious-both =
    Critical condition: consciousness {$consciousness}%, shock {$shock}%.
    You pass out — your pulse hammers in your temples as the world fades.
advanced-health-consciousness-returned = Consciousness returns. Breathe steadily.

advanced-health-chip-brain = Brain
advanced-health-chip-blood-loss = Blood loss
advanced-health-chip-ventilation = Ventilation
advanced-health-chip-hydration = Hydration
advanced-health-chip-nutrition = Nutrition
advanced-health-chip-radiation = Radiation

advanced-health-chip-pressure-tooltip = Systolic/diastolic arterial pressure, mmHg. Pulse rate is displayed separately.

advanced-health-chip-protection = Protection

advanced-health-chip-zone-equipment = ZONE EQUIPMENT
advanced-health-chip-no-zone-equipment = No equipment covers this area.
advanced-health-chip-equipment-rating-hint = Worn equipment associated with this area. Clothing class is not a damage-absorption percentage.
advanced-health-chip-armor-clothing = clothing
advanced-health-chip-armor-light = light armor
advanced-health-chip-armor-medium = medium armor
advanced-health-chip-armor-heavy = heavy armor
advanced-health-chip-hand-left = L. hand
advanced-health-chip-hand-right = R. hand
advanced-health-chip-hand-extra = Hand
advanced-health-chip-active-tool = In hand: {$item}
advanced-health-chip-empty-hand = empty
advanced-health-chip-slot-empty = Empty slot
advanced-health-chip-pulse = PULSE
advanced-health-chip-pressure = BP · mmHg
advanced-health-chip-pulse-value = {$value} /min
advanced-health-chip-rhythm-hint = Heart rhythm visualized from the heart rate. The trace fades to a flatline after cardiac arrest.
advanced-health-chip-shock-none = Shock load {$value}% — no marked signs of shock.
advanced-health-chip-shock-low = Shock load {$value}% — physiological stress; further deterioration may cause unconsciousness.
advanced-health-chip-shock-high = Shock load {$value}% — high risk of unconsciousness and decompensation.
advanced-health-chip-perfusion-low = Perfusion {$value}% — inadequate blood delivery to tissues.
advanced-health-chip-perfusion-normal = Perfusion {$value}% — blood delivery to tissues is maintained.
advanced-health-chip-infection-observation = Infection load {$value}% — signs of systemic infection.
advanced-health-chip-toxin-observation = Toxin load {$value}% — poisoning increases physiological stress.
advanced-health-chip-abdominal-procedure = Abdomen and pelvis share one map region. Choose the subsection for the gauges and treatment.
advanced-health-chip-bones = Regional bones
advanced-health-chip-organs = Regional organs
advanced-health-chip-bone-fractured = Bone integrity {$value}%. A fracture is present; stabilization is listed below.
advanced-health-chip-bone-intact = Bone integrity {$value}%. No fractures detected.
advanced-health-chip-organ-injured = Organ tissue integrity {$value}%. Internal damage is present.
advanced-health-chip-organ-intact = Organ tissue integrity {$value}%. No damage detected.

advanced-health-chip-bone-damaged = Bone integrity {$value}%. Bone tissue is damaged; a fracture is not confirmed.

advanced-health-chip-perfusion-short = Perfusion
advanced-health-chip-toxins-short = Toxins
advanced-health-chip-state-absent = Not detected
advanced-health-chip-state-danger = Danger
advanced-health-chip-state-stress = Stress
advanced-health-chip-state-insufficient = Insufficient
advanced-health-chip-state-maintained = Maintained
advanced-health-chip-state-detected = Detected
advanced-health-chip-storage = QUICK ACCESS
advanced-health-chip-active-hand = {$hand} · active
advanced-health-chip-hand-free = Empty

advanced-health-chip-infection-metric-hint = Systemic infection load. An increasing value indicates progression of infection.
advanced-health-chip-toxin-metric-hint = Systemic toxin load. An increasing value indicates accumulation of toxic substances.

advanced-health-chip-dead = Patient deceased
