# Generator and water-pump prospecting scorecard

Date: 2026-11-04

This scorecard is a deterministic prioritization aid for human review. It is not a probability of purchase, credit decision, or proof of contact validity. It must be versioned and its component evidence shown to the reviewer.

## 1. Scoring dimensions (100 points)

| Dimension | Max | Evidence standard |
|---|---:|---|
| Buyer / organization identity | 15 | Official registry, official buyer portal, or independently corroborated company website |
| Procurement intent | 30 | Live tender, recent award, public RFQ, importer/distributor sourcing notice, or explicit equipment requirement |
| Product fit | 20 | Explicit generator, genset, water pump, pump set, irrigation, dewatering, construction, rental-fleet or power-resilience requirement |
| Market / delivery fit | 10 | Target country/region, import capability, service footprint, voltage/frequency/emissions requirements and shipping feasibility |
| Recency | 10 | Original publication/update date; use retrieval date only when publication date is unavailable and label it |
| Evidence quality | 10 | Original source, stable record ID, direct source URL, field-level provenance and corroboration |
| Contact route quality | 5 | Public business contact channel on an official domain; personal email is not required for a qualified company |

## 2. Hard review gates

The following conditions prevent automatic promotion to the high-priority queue, regardless of total score:

- No identifiable organization or source record.
- Only AI-generated or search-snippet evidence exists.
- The source is inaccessible, materially contradictory, or cannot be attributed to a provider record.
- Product fit is inferred solely from a broad industry label.
- The record is a duplicate of an existing customer, lead, or candidate and no explicit reviewer override is supplied.
- The source appears to be a personal-data dump, credential leak, restricted-access content, or otherwise unsuitable for collection.

A missing contact email is not a disqualifier. A verified company and a documented procurement signal are more valuable than an unverified email address.

## 3. Product-fit taxonomy

### Generators

Positive direct signals include generator set, diesel generator, gas generator, standby generator, prime power, backup power, genset, generator rental fleet, power plant package, generator replacement, and explicit kVA/kW capacity requirements.

Capture when available: rated kW/kVA, phase, voltage, frequency, fuel, emissions stage, duty rating, enclosure, parallel operation, delivery location, installation/commissioning and service requirements.

### Water pumps

Positive direct signals include water pump, centrifugal pump, submersible pump, diesel water pump, irrigation pump, drainage/dewatering pump, firefighting pump, booster set, sewage/wastewater pump and pump station.

Capture when available: flow, head, fluid, solids handling, suction/lift, power source, material, connection size, duty cycle, installation environment and delivery location.

Do not infer a technical specification from a product family name. Unknown values remain unknown.

## 4. Recency bands

- 0–30 days: 10 points
- 31–90 days: 8 points
- 91–180 days: 6 points
- 181–365 days: 3 points
- Older than 365 days or unknown publication date: 0 points

The pilot can ingest older records for research, but they should not receive recency points. A fresh retrieval of an old tender does not make the tender recent.

## 5. Priority bands

- 80–100: Priority A — strong documented signal; human review first.
- 60–79: Priority B — plausible fit; review after A.
- 40–59: Priority C — weak or incomplete evidence; enrich only if cost-effective.
- 0–39: Hold — do not spend paid enrichment credits.

Priority is not approval. Only a human reviewer can approve a candidate for CRM conversion.

## 6. Required explanation

Every scored candidate must show:

- score and scorecard version;
- component points and the evidence record(s) supporting each component;
- hard-gate outcomes;
- unknown/missing fields;
- scoring timestamp and deterministic rule version.

If an AI model produces a narrative, show it separately as an unverified analysis and retain the evidence IDs supplied to the model. The model cannot alter deterministic component scores or evidence verification states.

## 7. Pilot evaluation

For the first pilot, manually label at least 100 records across generators and pumps (or all available records if fewer). Track precision at A/B thresholds, duplicate rate, evidence completeness, false procurement-intent rate, country/product distribution and reviewer time per candidate.

Do not optimize for raw candidate volume. The primary measure is the share of reviewed candidates with a verifiable organization and a genuine, relevant procurement/distribution signal.

The scorecard must be recalibrated from reviewer outcomes and versioned; never silently change the interpretation of historical scores.
