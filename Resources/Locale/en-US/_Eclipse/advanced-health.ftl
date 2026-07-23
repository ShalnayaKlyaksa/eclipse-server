advanced-health-part-auto = Auto
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
advanced-health-menu-bp = {$sys} / {$dia} ({$pulse})
advanced-health-menu-o2 = {$value}% O2
advanced-health-menu-wounds = Injuries
advanced-health-menu-equipment = Equipment
advanced-health-slot-hand = Hand
advanced-health-slot-belt = Belt
advanced-health-slot-back = Back
advanced-health-slot-pocket = Pocket
advanced-health-slot-suit = Suit
advanced-health-slot-id = ID

advanced-health-vital-immunity = Immune defense
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
