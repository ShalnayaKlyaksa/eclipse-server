# Clinical medicine ↔ pharmaceutical chemistry

Coordination contract, 2026-11-04. Chemistry owns synthesis, batch metadata and laboratory hardware. The clinical implementation owns diagnoses, ABO/Rh, transfusions and the medical chip. Do not replace clinical effects with another metabolism loop.

## Available API

Namespace: `Content.Shared._Eclipse.MedicalChemistry`.

- `MedicalBatchData : ReagentData`, `Purity` (integer 0..100): bound impurities are part of the reagent identity and survive ordinary solution transfer, pills, syringes and cloning.
- `MedicalBatch.GetPurity(ReagentId)` and `MedicalBatch.GetPurity(Solution, string prototype)` return 0..1. Untagged supplies are 1.0. Mixtures of batches are weighted by volume.
- `MedicalBatch.Take(Solution, string prototype, FixedPoint2 amount)` proportionally withdraws all grades of the selected medicine.
- `MetabolizerSystem` groups tagged grades by reagent prototype before applying effects and removing a dose. Impurity toxicity is applied there once per actual dose. Clinical sampling must not apply it again.
- Medical chemical reactions preserve the lowest volume-weighted quality of consumed tagged ingredients, including downstream medicines.

## Patient profile API (implemented and verified)

- `MedicalBatchData.PatientDna` is nullable. Null means a universal preparation. This is an in-game pharmaceutical profile, not ABO/Rh or blood compatibility.
- `MedicalBatch.GetActivity(Solution, string prototype, string? patientDna)` returns **effective units**, weighted by purity and patient affinity. An untagged universal drug returns its raw quantity. Matched personalised batches use 1.15 affinity; mismatched/unknown recipients use 0.65. Raw volume remains the overdose input.
- `MedicalSampleAnalysisData : ReagentData` marks a genuinely assayed reference sample. The analyzer requires a real sample and time/power; the synthesizer requires one unambiguous assayed `DnaData.DNA` identity before personalisation. No disease or blood-group facts are invented by this hardware.

`GetActivity` and the metadata types are now present in `MedicalBatchData.cs`. `GetMismatch(Solution, string prototype, string? patientDna)` returns the volume fraction of a drug which is personalised for another identity. Metabolism also applies this mismatch toxicity once; clinical sampling must not duplicate it.

Clinical integration should sample `(raw quantity, effective quantity)` independently for each reagent from bloodstream and metabolites, using the body's `DnaComponent.DNA`. Use effective quantity for therapeutic activity and **raw quantity** for overdose. Keep existing plain `ClinicalMedicationMath.Calculate` calls backward compatible. Do not independently remove reagents or re-apply bound impurity toxicity.

The analyzer accepts any `FitsInDispenser` sample, including `AdvancedClinicalBloodSample`'s `drink` solution. It assays 5 real units in 10 seconds and returns the assayed sample to its buffer for bottling. The synthesizer's `medicalSampleSlot` accepts the assayed vial. No dependency on `BloodModels.cs` or transfusion APIs is required. Preserve `DnaData` during collection; analyser/assay metadata belongs to this change.

## Clinical production hand-off

Chemistry has added advanced `medicalSynthesis` entries for the 12 `Clinical*` medicines and replaced the products of the existing `clinical_reactions.yml` finishing recipes with corresponding named pharmaceutical precursors. Existing bottled supplies remain actual drugs. `ClinicalPrecursor` remains the common chemical base.

Ordinary ChemMasters retain the three basic drugs. A research/cargo-obtainable pharmaceutical synthesizer handles clinical precursors and personalisation. A separate laboratory analyzer assays samples. Hardware, recipe gating and assay metadata belong to this chemistry change; leave blood models and transfusions to the clinical implementation.

## Verification

The combined build initially hit access-analyzer errors on chained calls through the restricted `BloodstreamComponent.BloodReferenceSolution` field. Resolved without changing blood/transfusion behavior: reference cloning first reads the solution into a local; reference-volume conversions use the existing `(float)` conversion rather than invoking `.Float()` through that field. `BloodModels.cs` and all clinical rules are unchanged by this chemistry work.

Use an isolated output directory directly under `bin/`, e.g. `bin/MedicalChemistryVerification/`. The normal server/debugger may hold output files open. A dirty-disposed/skipped NUnit test is not a pass; inspect TRX.

Chemistry's combined verification: normal build with analyzers enabled, zero errors; 46 unit tests and 45 integration tests passed, zero skips. The latter includes hardware/sample/personalisation, clinical medicine and blood integration, selected changed chemical reactions, and the client/shared sandbox check. Results are in `outputs/medical-chemistry-unit/` and `outputs/medical-chemistry-integration/`. Native screenshots of the actual Russian client are in `outputs/medical-chemistry-review/`; regenerate with `Tools/medical/review_chemistry.ps1`.
