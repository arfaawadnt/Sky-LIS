دليل LDM · LDM Field Guide

<!-- #prog -->

<!-- #doc -->

**LDM** Field Guide · National Technology

_Flag unconfirmed_

[DIAGRAM:  ]

[DIAGRAM:  ]

[DIAGRAM:  ]

ع EN

National Technology · Laboratory Data Manager 9.2.0

# LDM System Guide — Engineer Master Edition

From the moment the patient reaches reception until they leave with their sample labels and receipt — every step as it is done on screen, every rule as it actually behaves.

**System version** LDM 9.2.0

**Platform** Oracle Forms

**Guide version** 1.9

**Date** 2026-09-13

**Audience** National Technology engineers

9/12 Nine sessions complete: registration, results and search, the worklist and sample rejection, finance, test setup, privileges with analyser integration, PCR integration, and Auto Verification.** This edition covers the whole registration module (`Registration`, `Patient Tests`, `Payment`, `Samples`, `Home Reservation`, `Batch Registration`, `Lab To Lab`) and then the full result cycle (`Results By Accession No.`, `Results By Sample Id`, `Search By Patient Data`, the print list and `Abbreviated Report`) then `Worklist`, `Reject Sample` and `Unreject Sample`, with the concepts under them. The four remaining sessions (installation, database and branches) are out of this guide’s scope. Every step here was observed in the training recordings — nothing is invented.

<!-- nav -->

## Contents

1. 01How to use this guide
2. 02Login & main screen
3. 03Core concepts
4. 04Registering a patient
5. 05Home Reservation
6. 06Batch Registration
7. 07Lab To Lab
8. 08Results & Search
9. 09Worklist & rejection
10. 10Financial
11. 11Test setup
12. 12Users & privileges
13. 13Analyser integration
14. 14PCR integration
15. 15Auto Verification
16. 16Worked scenarios
17. 17Staff FAQ
18. 18Troubleshooting
19. 19Reserved chapters
20. 20Glossary
21. 21Needs confirmation

___Complete, sourced_ ___Partial_ ___Reserved — no source_

---

<!-- section c1 -->

## 01How to use this guideComplete



This guide is written for the person at the screen who needs to get something done — not for someone reading about the system. Every topic turns quickly into a **numbered procedure**, and every procedure ends with **what you should see on screen** if it worked.

### Three ways to read it

| You are | Start at |
| --- | --- |
| A new reception clerk | Chapters 02, 03, 04 in order, then try a chapter 16 scenario on a training build |
| A chemist or unit technician | Chapter 03 (statuses) then chapters 08 and 09 in order — entry, review, sign-off, search, printing, worklist, rejection |
| An accounts or cash-desk officer | Chapter 03 §4 (the contract sets the price) then chapter 10 — pricing, collection and refunds, closing the day, claims |
| An experienced user needing one step | The procedure index at the top of chapter 04, or the FAQ in chapter 17 |
| An installation or support engineer | Chapter 03 (the rules behind the behaviour), chapter 18, and chapter 21 (what is still unconfirmed) |

### Symbol key

| Symbol | Meaning |
| --- | --- |
| `Patient No.` | **A screen, field or button name **copied verbatim from the interface** — even when it carries an original typo (like `Shotcut` and `Remainig`) |
| `F10` | A key you press on the keyboard |
| Tip / Warning / Note / Common mistake | Coloured boxes: green eases the work, amber warns, red prevents a mistake people actually make |
| Needs confirmation | **Information not seen on screen or not stated by the source. **Never filled by guessing.** All collected in chapter 21; the flag toggle in the top bar highlights them at once |
| 00:29:25 | Timestamp in the training recording the fact was taken from (session 5 in the registration chapters, session 6 in the results chapter) |

### The keyboard — learn these first

**

LDM is an Oracle Forms application, which means its four most important commands **are keys, not buttons**. They work on every screen without exception, and without them you will hunt for a "Save" button that does not exist.

`F10`Save.** Saves the current screen. In registration it creates the accession number, triggers printing and opens Payment for cash cases.

`F9`List of Values.** Opens the choices for the field the cursor is in. Every orange field has one.

`F7`Start a search.** Clears the screen into query mode; type what you are looking for into any field.

`F8`Run the search.** Shows the results. `F7` then `F8` at once = the last saved record.

`F6`

**New record.** Starts a new row in a grid, or **clears the whole screen** ready for a fresh entry. After any search, press it to start a new case instead of closing and reopening the screen.

`F4`**`

**Duplicate the previous record.** After `F6` it fills the new record with a full copy of the one you were standing on. The fastest tool in the program for entering similar records.

`Enter`Validate and continue.** In `Patient Name` it checks for duplicates, in `Booking No.` it pulls the booking, in `Birth Date` it computes the age, in `Shortcut` it drops the test in.

[NOTE:TIP] The status bar tells you what you can do `

A thin strip runs along the bottom of the window. When the cursor sits in a field that has a value list, `List of Values` appears there — a signal that `F9` will work. Beside it, `Record: 1/1` tells you how many records a search returned.

---

<!-- section c2 -->

## 02Login and the main screenComplete

**`**`LDM** (`Laboratory Data Manager`) by **National Technology** runs the lab from patient reception to result delivery. The version documented here is `LDM 9.2.0`, running on **Oracle Forms** — which is why chapter one is about keys.

[PROCEDURE]

#### Logging in

**You need** A user name and password issued by the system administrator

`
1. Open LDM. The splash screen shows the National Technology logo, the version `LDM 9.2.0`, the licensed site name, and the login box in its corner. `
2. Type your name into `User Name`. `
3. Type your password into `Password`. 
4. Press `Login`. (`Cancel` closes the program.)

**Expected result**  The `System main menu` window appears, the top menu bar becomes active, and your name is written in the window title: `L.D.M User:ADMINISTRATOR`, for instance. If you see someone else's name there, you are working under their account.

[FIGURE]  [IMAGE img/img-001.webp — شاشة دخول LDM]

_Caption:_ The login screen.** The version and licensed site are printed on the screen itself. 00:00:12

### The main menu — 16 modules

`

Every screen in the system opens from one of these sixteen items. This guide covers the four after `Setup` in depth (`Home Reservation`, `Registration`, `Batch Registration`, `Lab. Registration`) and what branches off them.

[FIGURE]  [IMAGE img/img-002.webp — شريط القائمة الرئيسية بستة عشر موديولاً]

_Caption:_ The sixteen items, verbatim, left to right.

### Closing a screen with unsaved changes

**

If you close any screen holding data not saved with `F10`, the system asks: `Do you want to save the changes?` — `Yes` saves and closes, `No` closes **and discards what you typed**. It is the only guard between you and losing a whole registration, so read it before you click.

[FIGURE]  [IMAGE img/img-003.webp — حوار Do you want to save the changes]

_Caption:_ `The dialog over `Batch Registration`. 00:53:40

Which right governs which screen.** Permissions themselves are now documented in chapter 12: the system **ships no preset roles** — each lab builds its own groups, and the `Groups And Privileges` and `System Users` screens are covered in full. What remains unconfirmed is narrower: **which named right governs which screen** (marked `⚠️` in the procedure chapters), and the contents of `Rights`, `Rights Adminstiartion` and `Menu Management` — never opened in training.

---

<!-- section c3 -->

## 03Core conceptsComplete

Six rules govern everything you will do in registration. Read them once with attention and every screen's behaviour will make sense.

### 1 · The three numbers — patient, visit, tube

`

In the training example a patient named `TEST` was registered. He came out of the process with three numbers, each living at a different level:

| Number | In the example | Stands for | Changes? |
| --- | --- | --- | --- |
| `Patient No.` | 292010 | The patient's medical file — all visits, payments and results | Never** |
| `Acc. No.` | 1784 | This particular visit — its tests, samples and bill | New each visit, sequential |
| `Sample ID` | 22030000005
22030030002 | One specific tube and what runs on it | Unique, never reused |

**

The relationship is a hierarchy: one patient → many visits → many tubes per visit → many tests per tube. In practice: **if the patient asks about their whole history**, search by patient number; **if they ask about yesterday's result**, by accession; **if you are holding a tube**, by sample ID.

[NOTE:WARN] The patient number is auto-generated — do not type it **`

The system can work two ways: generate the number itself, or let you type it (a national ID or a phone). The source is explicit: manual entry **produces duplicate files** and should be avoided. In auto mode you leave `Patient No.` empty and the system fills it on validation. And since the number never changes once issued, any mistake here lives in the record forever.

### 2 · Why four tests become two tubes

**

In the training example the clerk picked four tests: `AST (SGOT)`, `Lipids Profile formula`, `Serum Uric Acid`, `CBC`. The system issued **only two tubes**:

| `Sample ID` | Unit | Sample type & condition | Tests on it |
| --- | --- | --- | --- |
| 22030000005 | `CHEMISTRY` | `Serum Fasting Refrigerated` | AST (SGOT) · Lipids Profile formula · Serum Uric Acid |
| 22030030002 | `HAEMATOLOGY` | `EDTA Blood` | CBC |

**

The rule: tests run in the **same unit** on the **same sample type and condition** merge into one tube. A different unit splits them even when the type matches (three `Serum Refrigerated` samples in the 2007 example came out as three numbers because they belonged to three units). A different type splits them even inside one unit. The clerk decides none of this — **the configuration in `Test Setup` does**.

[FIGURE]  [IMAGE img/img-004.webp — ملصق باركود العينة 22030000005]

_Caption:_ **`**The sample label as printed in training.** Left to right: the unit code `C` in a box, date/time and patient location, sample type and condition, accession 1784 and patient name, the sample number, and the test names running vertically on the right edge. 00:45:31

[FIGURE]  [IMAGE img/img-005.webp — تقرير ملصقات الباركود المعنون]

_Caption:_ The older example from company documents:** one visit produced six labels for five tubes (`C` · `H` · `R` · `R` · `I` · `M`); the two identical labels are two copies for one tube.

Sample ID length.** The concepts document says 10 digits; the 2007 label carries 8; the 2022 training build carries 11. It varies by version or setting — never build a manual check on it. Unit code names (`C` = Chemistry is confirmed by the `Samples` screen; `H`/`R`/`I`/`M` are inferred) need an official list.

### 3 · Sample status and test status — two tracks and a gate

`**

Every visit has two tracks moving in parallel: the **tube's status** (physical: ordered, drawn, arrived at the unit) and the **test's status** (medical: ordered, result entered, reviewed, signed off). Between them a hard rule: **no result can be written until the tube is `Received`**.

[DIAGRAM:  SAMPLE STATUS — the tube  1  Ordered  2  Collected  3  Received  label the tube and scan it  in phlebotomy  scan at the workstation  inside the lab unit  Reserved  cannot draw yet  Rejected  tube unfit for testing  hard gate — no result may be recorded until  Sample Status = Received  TEST STATUS — the examination  1  Ordered  2  Complete  3  Review  4  Verification  e-signature  enter results + Save  first-pass check  doctor signs off + Comments  ]

_Caption:_ Upper track physical, lower track medical.** The dashed line is the gate. Amber (`Rejected`) is a sample fault; dashed grey (`Reserved`) is a deliberate hold the clerk sets at registration.

| Status | Means | What moves it |
| --- | --- | --- |
| `Ordered` | Ordered (sample or test) — the default at registration | Saving the registration |
| `Collected` | The tube is drawn and labelled | Scanning the barcode in phlebotomy |
| `Received` | **The tube reached the unit — **the gate** | Scanning it at the unit workstation |
| `Complete` | The result is entered and saved | Entering the result + save |
| `Review` | First-pass review | The reviewer |
| `Verification` | **Doctor's sign-off — **the e-signature** | `The responsible doctor, with `Comments` |
| `Reserved` | Deferred — the patient hasn't met the test's condition (fasting, say) | `The clerk from `Status` in `Patient Tests`; set back to `Ordered` later |
| `Rejected` | Unfit sample (clotted, haemolysed, condition unmet) | After the draw or after receipt |

[NOTE:WARN] Know whether your lab runs Auto Receive `

A setting makes the sample `Received` **automatically at the moment of saving** — used in labs that do not track samples by scanning. The training build runs it: on the `Samples` screen both tubes showed `Received` right after registration. In this mode the status **does not prove** the tube arrived; it is merely permission to enter a result. The source says `auto collect` / `auto receive` and never shows its screen.

**`The profile rule:** a test package (`Profiles` / `Mega Profiles`) does not advance to `Complete`, `Review` or `Verification` until **every** individual test under it has — one lagging test holds the whole profile.

[DIAGRAM:  Test A  Complete  Test B  Complete  Test C  Ordered  every test  at that status?  Profile  Ordered  one test behind  ⇒ the profile holds  ]

_Caption:_ `One test still `Ordered` ⇒ the profile holds.

### 4 · The contract sets the price — Rank and Relative

`**`**

Two fields that look administrative and are in fact the whole pricing engine. `Rank` is the **contract** the patient is registered under (the source's example: "the doctors' syndicate"), and `Relative` is **a tier under that contract** that may carry its own price or discount (a 20% tier, say). Every test chosen afterwards shows two prices: `Patient` (what the patient pays) and `Insurance` (what the payer covers) — both come from the contract.

Rank (contract) ▸ Relative (tier) ▸ price list ▸ Patient share + Insurance share

[NOTE:BAD] A wrong Rank/Relative is a wrong invoice `

The training default is `1 Normal` / `1 Normal Patient` (cash, no contract). If a contract patient is registered on the default, they pay full price and the payer never gets its invoice. Check both fields before `F10` — after saving you are in a financial path that is harder to undo.

[NOTE:INFO] Where the contract itself is set up **`

Creating a `Rank` and `Relative`, tying them to a price list and computing discounts are all in **chapter 10** (`Main Price List` · `Price Lists` · `Client Price List`). Here you only pick the contract; there you build it.

### 5 · The patient's condition changes what "normal" means

`**

The `Patient Condition` field selects the **reference range** (`Reference Range`) the patient's results will be judged against. A pregnant woman has ranges unlike the general ones, a diabetic has his own, a smoker affects certain tests. The conditions themselves are defined in setup (a `Conditions` screen with `Serial` and `Condition` columns — empty in training) and the clerk picks one at registration.

[NOTE:BAD] Skipping the field makes a result read wrongly 

If no condition is set, the result is compared to the general range: a **normal** value for a pregnant patient can be flagged "out of range" or the reverse. The field is filled **at registration** — it is not fixed after the result appears.

### 6 · Field colours are not decoration

On every screen, the colour of a field's label tells you its rule before you touch it:

| Colour | Rule | What you do |
| --- | --- | --- |
| Red | Mandatory.** The registration will not save without it | `Fill it — e.g. `Patient Name`, `Birth Date`, `Sex` |
| Orange | **Has a **list of values** | Press `F9` and pick — e.g. `Nationality`, `Doctor`, `Location` |
| Blue | **`Its requirement is **configurable** from `System Parameter` | May be mandatory at your lab — e.g. `Tel. No.`, `Rank` |
| Black | Optional | Fill it if the information is available |

`

The source's example for blue: the system can be set to refuse a registration until a mobile number is entered or a doctor is chosen — from `System Parameter`.

### 7 · Two ways to search — and they never work together



Every LDM screen searches in one of two ways, and knowing which applies to the screen in front of you spares you the most common beginner's mistakes: pressing `F7` on a screen that ignores it, or hunting for a `Search` button on a screen that has none.

|  | 1 · Query Mode | 2 · Search buttons — Search / New Search |
| --- | --- | --- |
| The idea | The entry or display screen itself turns into a search screen | A dedicated screen carrying explicit `Search` and `New Search` buttons |
| Activation | F7`, or the `New Query` icon on the small toolbar at the top. The status bar reads `Enter-Query` | Nothing — you type your criteria straight into the fields |
| Execution | Type the variable (or several at once) into its fields, then `F8` (`Perform Query`) | **The `Search` button; for a fresh search `New Search` — **only** through the two buttons |
| Results and navigation | **Records come in sequence, **newest first**; move between them by clicking a record then the down arrow, or with the `Next Record` / `Previous Record` toolbar buttons | `A list in a grid, each row opened by its own button (`R`, say) |
| Examples | `Registration` · `Results By Accession No.` · `Results By Sample Id` · `Reject Sample` | `Search By Patient Data` · `Worklist` |

[NOTE:WARN] The compatibility rule: the buttons disable the keys **

If a screen has `Search` and `New Search`, then `F7` and `F8` are **disabled on it and do nothing**. If a screen supports searching and has no such buttons, it relies on `F7` and `F8`. No screen works both ways.

`

#### The `%` sign — searching for what you are not sure of

`

In every text field of every search screen, `%` stands in for any uncertain segment or letter. It has no fixed position: at the start, the middle, the end, or between parts.

| The doubt | What you type |
| --- | --- |
| Is there a space between "عبد" and "الله"? | `محمد عبد%الله` or `محمد%عبد%الله` |
| Was a middle name recorded? ("محمد أحمد النبراوي" or "محمد أحمد محمد النبراوي") | `محمد%النبراوي` |
| Does the name end in "ي" or "ى"? | Drop the last letter and put the sign in its place: `النبراو%` |
| `A long value list (`F9`) and you know part of the name | `%نص%` · `نص%` · or its start and end `أول%آخر` — the same principle in every value list |

[NOTE:BAD] `Critical: a `%` typed in entry mode is saved as data 

Be certain you are in search mode (`F7` or `New Search`) before typing `%`. If you are in data-entry mode, type it by mistake and press `F10`, the sign is stored as a real value in the database instead of a search being run.

### 8 · What a "record" is on each screen — the logic behind the screen split

**

A common mistake: many never grasp what the screens are split on, nor that **the unit of a record differs from screen to screen** according to its operational job. Four screens show "the same" data — each cuts it differently:

| Screen | One record = | Governing logic |
| --- | --- | --- |
| `Results By Accession No.` | One independent report that will print** (every test inside that report) | Opens in query mode. Tests are grouped by `Clinical Group` to print together, except a `Mega Profile`, which prints as a separate report even with a single test that shares a unit with others. Each record has a `Print` button, and what you see is exactly what prints. Records are split by report type/name. A name search with `%` brings every matching visit, even across patients |
| `Results By Sample Id` | One tube** | `Shows the tests listed under one specific `Sample ID`, and only those |
| `Search By Patient Data` | One report** meeting the criteria | `**Displayed exactly like `Results By Accession No.`; **the number of records equals the number of matching reports**. The `R` button opens that very report to view all its samples. One record may hold several `Sample ID`s and several tests |
| `Worklist` | One test** (by test) | **A test may have its own work cycle, so the screen prints a worklist of the required test per patient. **The number of records equals the number of tests** matching the criteria across every accession |

In short:** `Results By Accession No.` at report level · `Results By Sample Id` at tube level · `Search` lists reports by criteria and opening a record opens that report with all its samples · `Worklist` at single-test level across all accessions. National Technology material 2026-09-09

### 9 · Every screen has two paths: add or edit?

**

This is the one thing to settle before opening any LDM screen — registration, setup, prices, tests, any of them. The same screen serves two different jobs, the route to each is entirely different, and confusing them means either editing a record you meant to create or hunting for a "New" button that does not exist. **Decide before you type a character: am I creating something new, or editing something that exists?** National Technology material 2026-09-10

| You want to | The route |
| --- | --- |
| Add new** | **Open the screen and make sure you are on a **new empty record** (`F6` guarantees it) → fill the fields → `F10` |
| Edit existing** | F7` (query mode) → type your criteria into any field → `F8` → change what you need → `F10` |



The rule is the same on every screen, and it is the same one described in 7 about searching: `F7` then `F8` means "open an existing record", and nothing else does.

#### Three kinds of field — each with its own way in

Filling a screen you will meet only three kinds, handled identically everywhere in the program. The field's colour tells you how important it is (see 6); its kind tells you how to put a value in:

| Kind | How you enter a value |
| --- | --- |
| Text field | Just type into it. |
| Combo box | Open the ▾ arrow and pick from what is offered. Do not type into it. |
| List of Values | `Stand in the field and press `F9`. The status bar writes `List of Values` to tell you the field is of this kind. Searching inside the list accepts `%` as a wildcard. |

[NOTE:BAD] 🚫 Never hand-type into a field that has a list **

A field with a value list holds a **link** to another record behind it, not text. Typing may look accepted and may even save, but the link is never formed — leaving a record that looks fine and behaves as if broken. The clearest documented case: a hand-typed `Sample Condition` on the `Tests` screen produces a test that never appears at registration and prints no barcode, repairable only from the database (chapter 11 §2).

[NOTE:TIP] Why did the save refuse? Mandatory fields `**

`F10` succeeds **as soon as the mandatory fields are complete**, and not before. On most screens the mandatory ones are written in red (6). So when a save refuses with no clear message, look for an empty red field before anything else.



#### `F6` then `F4` — the fastest way to enter ten similar records

`

When the records you are entering mostly repeat — ten tests in the same group with the same sample type and condition, say — do not retype the shared part ten times. `F4` copies the previous record wholesale. National Technology material 2026-09-10

[PROCEDURE]

#### Entering similar records by duplication

**Works on** Any full data-entry screen, and any screen with a grid that accepts a new record

**Prerequisites** One record already entered correctly, to be the original you copy from

**
1. Stand on the **original record** whose data you want to copy. `
2. `F6` — the screen clears and a new record opens. `
3. `F4` — the previous record's data drops into the new one in full. **`
4. Change **only what differs**. On the `Tests` screen that means: `Test Name` (which must be unique), `Report Name`, how it shows on the report, and the reference range. 
5. `F10` to save, then repeat the sequence for the next one: `F6` → `F4` → change → `F10`.

**What you should see**A new record already filled with everything shared, into which you typed only the differences, and `Data has been saved successfully.` in the status bar after each `F10`.

[NOTE:WARN] `What `F4` copies includes what you did not want **`

The copy is **complete**: it includes fields that must be unique, the reference ranges and the durations. Check the new record before `F10` — a test with a unique name but a reference range copied from another is a silent error that surfaces only at the first patient result.

---

<!-- section c4 -->

## 04Registering a patient — step by stepComplete

`

This is the hands-on chapter. It starts with which screen to pick, then walks one patient from opening `Registration` until the labels and receipt come out — and then returns to every branch (returning patient, deferral, discount, partial payment, reprint, edit).

### Procedure index

| # | Procedure | Use it when |
| --- | --- | --- |
| 4.1 | Pick the right screen | Before any registration |
| 4.2 | New patient — patient data | First visit to the lab |
| 4.3 | Returning patient — similar-name warning and search | The patient has a file |
| 4.4 | Choosing tests — three ways | After the patient data is complete |
| 4.5 | Reading precautions and deferring a test | A non-fasting patient, say |
| 4.6 | Removing a test and applying a discount | Before saving |
| 4.7 | Saving and what prints | The end of every registration |
| 4.8 | Collecting payment — full, partial, card | A cash patient |
| 4.9 | The Samples screen — print one label or the job order | After saving |
| 4.10 | Retrieving a saved visit and editing it | Adding a test, reprinting, settling a balance |
| 4.11 | Attaching a document — Scan Attach | A prescription or ID card |

### 4.1 · Pick the right screen

There are three registration screens plus a booking screen. Picking the wrong one is the first thing that stalls a new clerk:

| Screen | Use it when | What sets it apart |
| --- | --- | --- |
| `Registration` | One patient in front of you — the ordinary case | All the fields, the similar-name check, `Patient Tests`, `Scan Attach` |
| `Batch Registration` | **A group **sharing the same tests** — a company's staff | A shared contract and location on top, one row per person, tests chosen once for all, group payment |
| `Lab. Registration` | **Samples **arriving from a contracted lab** | You pick the sending lab and its data fills in; tests are chosen on the same screen |
| `Home Reservation` | Not registration** — booking a home visit | `Produces a booking number used later in `Registration` |

### 4.2 · New patient — the patient data

[FIGURE]  [IMAGE img/img-006.webp — شاشة Registration]

_Caption:_ **`**The screen as it opens.** Note the colours: red mandatory, orange has a list, blue configurable. The greyed buttons at the bottom (`Preparation`, `Sampling Details`, `Add Patient`) enable after saving. 00:12:54

[PROCEDURE]

#### Entering a new patient's data

**Path** `System main menu > Registration`

**Prerequisites** None. If you hold a home-booking number, jump to chapter 05

**Typical time** One to two minutes

**
1. Open `Registration`. The cursor sits in `Patient No.` — **leave it empty** and move on. The system will write the number itself. 
2. If the patient has a condition that calls for special ranges (pregnant, diabetic, smoker), choose `Patient Condition` with `F9`. Otherwise skip it. `**
3. Open the `Title` list and pick one. It holds English titles (`Mr`, `Mrs`, `Miss`) and Arabic ones (`الأستاذ`, `الأستاذة`, `الدكتور`, `الآنسة`…). **Picking the title fills `Sex` automatically**: `Mr` → `Male`, `Miss` → `Female`. `
4. Type the name into `Patient Name` — one, two or three parts — and press `Enter`. If a `similar name exist` message appears, follow procedure 4.3 and come back. `**`
5. In `Birth Date` type the date and press `Enter`: the system computes the age into the three boxes `DMY` (days / months / years). **If the patient doesn't know the date**, type the age straight into `Y` and the system derives the birth year. `
6. Check `Sex`. If you skipped the title, set it yourself from the list — it is red and the registration will not save without it. `
7. `Location`: in a standalone lab the default is usually `Out Patient \ خارجى`; in a hospital pick the department (emergency, inpatient, ICU…) with `F9`. `Room No.` and `Treatment No.` (hospital file or insurance number) are hospital fields too. **`
8. **Stop at `Rank` and `Relative`.** The default `Normal` / `Normal Patient` means cash at full price. If the patient is under a contract (syndicate, company, insurance), pick their contract and tier with `F9`. These two fields set every test's price from here on. `
9. Type the phone into `Tel. No.`. Phone search is what saves you when the patient returns without their number, and it may be mandatory at your lab (blue). 
10. If there is a referring doctor, choose them in `Doctor` with `F9` — the system builds doctor statistics on it later. `
11. Optional fields as needed: `Nationality` (`F9`), `Governorate` / `City` / `Area`, `Address`, `Email` (for sending results), `Comments` (any note — a different WhatsApp number, medication…). The `V.I.P` / `Urgent` / `Biohazard` flags if relevant. `
12. Press `Patient Tests` to move on to choosing tests (procedure 4.4).

**What you should see** Patient No.` is filled (in training: 292010), the age is computed in `D M Y`, and `Reg. Date` carries the current date and time. A `Comments` button appeared beside `Patients Count`.

[FIGURE]  [IMAGE img/img-007.webp — شاشة Registration مكتملة]

_Caption:_ Once complete.** Birth date 24-11-1995 gave 11 D · 3 M · 26 Y. An Arabic title (`الأستاذ`) and `Male`. 00:26:30

**Quick reference: what every field on the screen means**

| Field | Meaning and behaviour |
| --- | --- |
| `Patient No.` | File number; auto-generated |
| `Patient Condition` | Selects the results' reference range |
| `Title` / `Sex` | The title fills the sex automatically |
| `Birth Date` + `D M Y` | Date → age, or age → birth year |
| `Location` / `Room No.` / `Treatment No.` | Hospital fields: department, room, hospital file or insurance number |
| `Rank` / `Relative` | Contract and tier — they set the price |
| `Doctor` | Referring doctor, for doctor statistics |
| `Acc. No.` | Accession — filled on save |
| `Booking No.` | `Home-booking number; type it + `Enter` to pull the data |
| `Patients Count` | Today's case counter; resets daily |
| `Diagnosis` / `Diagnosis Tests` | `A diagnosis tied to a test package (`Diagnosis` screen: `IDC Code`, `Diagnosis Name`, a tests grid) |
| `Checkup` / `Checkup Days` | A recurring check-up that pulls its tests every N days (coagulation every 7 days, say) |
| `Comments` / `Email` / `Address` | Free notes; email for sending results |
| `V.I.P` / `Urgent` / `Biohazard` | Flags on the visit |

The screen is customised per client; if a field is missing at your site, your build is customised — the guide is not wrong.

### 4.3 · Returning patient — the similar-name warning and searching

The riskiest moment in reception is this one: a patient who has a file, and a clerk about to create a second one. The system has two ways to stop it.

[PROCEDURE]

#### Way one — the "similar name exists" warning while typing

**When** You type `Patient Name`, press `Enter`, and the system finds a similar name


1. A `similar name exist` message offers two options: pick from the list, or `New Patient`. 
2. Open the list. Beside each similar name: the **patient number** and the **phone number**. **
3. Ask the patient for their phone and **compare name and phone together**. Names coincide often; the phone decides. `**`
4. If they match, select the record. All its data comes down — `Location`, `Rank`, `Comments`, `Email`, `Address` — and the visit is registered under the **same** `Patient No.
5. If it really is someone else, choose `New Patient`.

**What you should see**The old patient number in `Patient No.` and their data in the fields; just review it, update what changed (a new phone, say), then `Patient Tests`.

[NOTE:BAD] Choosing New Patient without checking makes two files

Two files for one person means a history split in two, old results that never show when new ones are compared, and two numbers that cannot be merged afterwards. It is the most common damage haste does.

`This feature is **an option enabled in setup** and a recent addition that may be absent in some versions; it is **not applied** in `Batch Registration` (typing an existing name there registers a new patient with no warning).

[PROCEDURE]

#### Way two — searching with F7 / F8

**Use it** To find a file or visit before typing anything, or to retrieve a saved visit


1. In `Registration` press `F7`. The fields clear and the screen enters query mode. 
2. Type what you know into its field: the patient number in `Patient No.`, the accession in `Acc. No.`, the name in `Patient Name`, or the phone in `Tel. No.`
3. Press `F8`. Matching records appear; the status bar reads `Record: 1/3`, say — step through them with the toolbar arrows.

**A useful shortcut`F7` then `F8` at once with nothing typed = **the last visit registered**. That is how you get back to the case you saved a moment ago to print or edit.

### 4.4 · Choosing tests — the Patient Tests screen

`

This screen turns "patient data" into "a test order". Its window title carries the patient's name: `Patient Tests - TEST`. It has four regions:

[FIGURE]  [IMAGE img/img-008.webp — شاشة Patient Tests]

_Caption:_ **`**`**`Top:** `Groups` — the units (`CHEMISTRY` selected; `HAEMATOLOGY` shaded because a test of its is chosen). **Middle:** `Tests` — the selected group's tests, chosen ones shaded. **Bottom:** `Selected Tests` with each test's status, price and result date. **Right:** `Shortcut` and `Total Required` = 170. 00:33:20

[PROCEDURE]

#### Adding tests — three ways, pick what suits you

**Path** `Registration > Patient Tests`

**The source's advice** Way two for daily work; way three if your lab has set it up

**`
1. **Way one — browsing.** Click a group in `Groups` (say `CHEMISTRY`), its tests appear in `Tests`, then click the test name. Fine for exploring; hard daily, because it assumes you know which group every test lives in. **`**
2. **Way two — by part of the name.** Put the cursor in `Shortcut` (right panel), press `F9`, type three letters of the test and the list narrows (`AST` → `AST (SGOT)`; `LIP` → `Lipids Profile`), then `Enter` on the one you want. **This is the everyday method.
3. **Way three — by shortcut number.** If your lab has assigned numbers to common tests (the source's example: `AST` = 1, `CBC` = 100), type the number into `Shortcut` and press `Enter` — the test drops in at once. The fastest way, and it reduces to a sheet of numbers pinned beside you. 
4. Repeat for each test. Watch `Total Required` and `T. Count` update with every addition. 
5. For a test package (`Panels`) choose the package name; it is priced as a unit, cheaper than its tests added up. To see its tests press the `Tests` button on the right — the same button lists every selected test in one view when they exceed twenty and no longer fit the screen.

**What you should see**`Each test as a row in `Selected Tests` with status `Ordered`, the `Patient` and `Insurance` shares of its price, and `Result Date` — the expected result time (it differs per test: in training `CBC` was next day, the rest same day). In the example: four tests, `Total Required` 170.

[NOTE:TIP] Never type a test's full name

Three letters and `F9` are enough, and they stop you picking a similarly named test. The field here is `Shortcut`, while the column on the `Diagnosis` screen reads `Shotcut` — the system itself is inconsistent.

`**The `Tests Methods` button** beside each selected test was neither explained nor pressed in training. And the contents of the **groups** `Additional Services` and `Panels` were never opened.

---

<!-- section c4b -->

### 4.5 · Reading a test's precautions — and deferring it if needed

**

Some tests carry conditions: fasting, stopping a drug, a gap after a meal. The system knows this from the test's setup and shows it to you **at reception** — because you are the only one who still has the patient in front of them. If the warning only surfaced in the lab, the patient would be gone and the sample drawn wrongly.

[PROCEDURE]

#### Read the warning, then defer the test

**Path** `Patient Tests > Selected Tests > Status`

**The source's example** `A non-fasting patient and a `Lipid` test that requires fasting

`**`
1. In the `Tests` list, notice the tests shown **highlighted** — the highlight means precautions are configured for them.
2. Open the highlighted test to read its warning, and ask the patient about it now. `
3. If the patient meets the condition, add the test normally. If not: add it, then on its row in `Selected Tests` open the `Status` list and choose `Reserved`.
4. Carry on with the other tests and save normally. The deferred test is recorded and not forgotten, and no sample is issued for it. **`
5. **When the patient returns having met the condition:** retrieve the visit (`F7`, the accession, `F8`), enter `Patient Tests`, and change the test's status from `Reserved` to `Ordered`. The normal track resumes: the sample is drawn and its labels print.

**Why defer instead of delete**Because deleting drops the test from both the order and the bill. Deferring keeps it in the same visit, so when the patient returns nothing is re-registered and nothing is charged twice.

### 4.6 · Removing a test — and applying a discount

[PROCEDURE]

#### Removing a selected test

**Permission**A separate right** granted or withheld per user

`
1. In `Selected Tests` highlight the test's row. **`
2. **Double-click** it. It leaves the list and `Total Required` is recalculated.

**The source does not say **when** the delete right closes: after saving? after the draw? after payment? Nor does it show the refusal message for someone without it.

[PROCEDURE]

#### Applying a discount — as a percentage or an amount

**PermissionNeeds a right; recent versions set a **cap** per user (10%, say)

**Where** `The right panel of `Patient Tests`: `Discount` and `Disc. Per.`

**
1. **By percentage:** type it into `Disc. Per.` and press `Enter`. The source's example: 50 on a total of 170 → 85 due. **
2. **By amount:** type it into `Discount` and press `Enter`; the system computes the percentage. The source's example: 70 on 170 → 41.18% and 99.99 due.

**What you should see**`Both fields update together (`Discount` 70.01 / `Disc. Per.` 41.18) and the discount carries into `Payment` after saving. The one-cent difference (70.01 instead of 70) is percentage rounding — normal.

[PROCEDURE]

#### Recording who drew the sample — Done By

**Why** To compute the phlebotomist's home-visit share, and to know who drew a given sample

`
1. On the test's row move to the `Done By` column.
2. Pick the user who drew the sample from the system's user list.

### 4.7 · Saving — and what happens the next second

`

There is no "Save" button. Saving is the `F10` key, and it fires a chain of events you should know so you understand what comes out of the printers and why the Payment screen suddenly appears.

[PROCEDURE]

#### Saving the registration

**Before you press** `Check: the selected tests, `Rank`/`Relative`, any needed `Reserved`, and the discount

`
1. Press `F10`. 
2. The system creates the accession (`Acc. No.`) and the sample numbers, and the `Money` and `Labels` buttons in `Patient Tests` plus `Sampling Details` in `Registration` become active. `
3. Printing starts automatically — per the lab's `System Parameter` setup: the **barcode labels** on the printer named "barcode" (one per tube, copies configurable), and the **job order** (`Job Order` — a printout titled `Sampling Details`) on the default printer. **`
4. If the patient is **cash** (no contract) the `Payment` screen opens at once — procedure 4.8. For a contract patient it does not appear, or appears with nothing due.

**What you should see**`In training: `Acc. No.` 1784, two labels (two tubes), a job order stating "`This is the visit No. 2`" because it was patient 292010's second visit, and a `Payment` screen asking for 99.99.

[FIGURE]  [IMAGE img/img-009.webp — أمر العمل المطبوع]

_Caption:_ The job order travels with the tubes into the lab.** Its header holds patient, visit and balance due; its table ties each test to its unit, result time, sample and barcode. At the bottom, "draw time" and "drawn by" signature boxes. 00:47:35

[NOTE:INFO] No barcode printer? The number prints as text`

The training build had no label printer, so the label went to PDF with 22030000005 as text instead of bars. Bars need the `Code 128` font installed and Oracle Reports 6.0 — the engineer's concern, not the clerk's, but if you see a number without bars, that is why.

### 4.8 · Collecting payment — full, partial, card

[FIGURE]  [IMAGE img/img-010.webp — شاشة Payment]

_Caption:_ **`**`**Top:** `Patient Data` — who, how much (`Total Required`), the discount, what was paid, and `Remaining`. **Bottom:** the `Payment` grid — one row per payment: `Date`, `Paid` (red: mandatory), `Visa`, `Transaction No.`, and the `Print` and `E` buttons. 00:38:00

[PROCEDURE]

#### Collecting the amount and printing the receipt

**The screen appears** `Automatically after `F10` for a cash patient; or from `Money` in `Patient Tests` later

**Permission** Seeing the screen is a right of its own

`
1. Read `Remaining` — that is what you ask the patient for (99.99 in the example). **`**
2. On the first empty row of the `Payment` grid type the amount into `Paid`. **In full**: the same as `Remaining`. **Partially**: only what they paid (50, say) — the balance stays on the visit to be paid later. 
3. If paying by card: tick `Visa` and type the transaction reference into `Transaction No.`
4. Press `F10`. The payment is saved, the receipt prints automatically, and `Remaining` updates. 
5. For manual printing: `Print` on a payment row reprints its receipt, `Print All` every receipt, `Print Invoice` the invoice, `Print Barcode` the labels from here too. `
6. `Back` to return.

**What you should see**The payment row with its date, `Paid` equal to the payments' sum, `Remaining` zero for full payment. The receipt in the user's configured language (Arabic or English).

**`The `Recovery` field and the `E` button were not explained. The source says a **refund** exists in some versions but did not show it. For contract patients a "result receipt" can print via a separate setting — name transcribed from speech.

### 4.9 · The Samples screen — one label or the job order

`

After saving, the `Sampling Details` button in `Registration` becomes active and opens the `Samples` screen — the real picture of what you are about to draw: every tube, what is on it, and its status.

[FIGURE]  [IMAGE img/img-011.webp — شاشة Samples]

_Caption:_ `**Top:** `Samples` — per tube: `Unit`, `Sample Type`, `Status`, `Status Date`, `Sample ID`, `Labels` (copies), and a `B` button. **Bottom:** `Sample Tests` — the selected tube's tests and each one's type (`Test` or `Profile`). 00:46:15

[PROCEDURE]

#### Printing one tube's label, or the job order

**Path** `Registration > Sampling Details`

**Use it** A label got damaged, the printer was out of stock, or you want a fresh job order

`
1. Open `Sampling Details`. `
2. For one tube's label: press the `B` button on its row. Only that sample's label prints. 
3. For all labels at once: go back to `Patient Tests` and press `Labels`. 
4. For the job order: press `Print` at the bottom of `Samples`. It goes to the default printer.

### 4.10 · Retrieving a saved visit and editing it

Three needs bring you back to an old visit: the patient wants an extra test, is settling a balance, or you need a reprint. The route is the same:

[PROCEDURE]

#### Retrieving the visit and acting on it


1. In `Registration` press `F7`, type the accession into `Acc. No.` (or the patient number, name or phone into its field), then `F8`. For the last visit you registered: `F7` then `F8` at once. **
2. **To add a test:** `Patient Tests` → add in the usual ways → `F10`. Labels print for any new tubes the test requires. **`
3. **To settle a balance or reprint a receipt:** `Patient Tests` → `Money`. All previous payments appear; add a new row or press `Print` on an old one. **`
4. **To reactivate a deferred test:** `Patient Tests` → `Status` from `Reserved` to `Ordered` → `F10`. **
5. **To reprint:** `Labels` for the labels, `Sampling Details` → `B` for one label or `Print` for the job order.

**Remember**`Every edit is committed with `F10`. Close the screen first and the system asks `Do you want to save the changes?` — and `No` throws the edit away.

Not shown:** editing the patient's own data after saving (correcting a name or birth date), cancelling a whole visit, and what happens to labels already printed when a test is deleted. Awaiting later sessions.

### 4.11 · Attaching a document to the patient's file — Scan Attach

[PROCEDURE]

#### Attaching a prescription or an ID card

**Path** `Registration > Scan Attach
1. Press `Scan Attach`. 
2. Point to the file — an image or a `PDF`. The source prefers `PDF`.
3. Attach it. It is stored in the database with the patient's file and can be viewed or downloaded on any later visit.

[NOTE:WARN] The attachment stays at the branch that made it**

Attachments **do not travel between branches** in the routine transfer because they are large. One made at a branch is invisible to another — except those made on the main database. The developer can enable transfer.

---

<!-- section c5 -->

## 05Home ReservationPartial

**`

A patient asks for the phlebotomist to come to their home. You don't register them — you **book** them. The booking yields a number, the phlebotomist leaves with a printout, and when the sample reaches the lab the booking becomes a registration with a single `Enter`.

[FIGURE]  [IMAGE img/img-012.webp — شاشة Home Reservation]

_Caption:_ `**`**`**`**`Reservation Data`:** the patient's details and booking time. Note that `Address` is **red (mandatory)** here — unlike `Registration` — because the phlebotomist will go there. In the corner, a `Cancelled` box. **`Reservation`:** the `Test Names` list of requested tests. 01:05:30

[PROCEDURE]

#### Booking a home visit

**Path** `System main menu > Home Reservation`

**You need from the patientName, age or birth date, phone, **address**, and the tests wanted

**
1. Open `Home Reservation`. `Booking Date` fills with the current time — **change it to the requested visit time** (tomorrow 8 pm, say; 24-hour clock: 20:00). That is when the phlebotomist will go. 
2. For a patient with a file: type their number into `Patient No.` (or search with `F9`). For a new one: leave it. `
3. Choose `Title` (fills `Sex`), type `Patient Name`, enter `Birth Date` or the age in `Y`. 
4. Type `Tel. No.` and the full `Address` — the phlebotomist navigates by it. `
5. Set `Rank` and `Relative` (the contract) and `Patient Condition` if needed. `**
6. In `User Name` pick **the phlebotomist or courier who will make the visit** — this field assigns the visit to a person. `
7. In the `Reservation` section enter the requested tests into `Test Names` (`F9` and pick). `
8. Press `F10`.

**What you should see**`**`A `Booking No.` appears (in training: 140). In the standard build a **preliminary job order** and **preliminary labels carrying the booking number** — one per tube the phlebotomist will draw — print with the booking, and the tests' prices show. **A booking is not a registration:** search for the patient in `Registration` now and you won't find them; they appear once the booking is converted in the next procedure.

[NOTE:INFO] The training build printed no preliminary labels**

The trainer says the demo build wasn't configured to print the job order and labels from the booking screen or to show prices there, and that this **exists in the standard build**. If no labels come out of a booking at your site, that is configuration, not your mistake.

[PROCEDURE]

#### Turning the booking into a registration when the sample comes back

**Path** `Registration > Booking No.
1. Open `Registration`. 
2. Type the booking number into `Booking No.` and press `Enter`.
3. All the patient's data drops in from the booking: title, name, age, sex, phone, address, contract. Review it, add or change what's needed. 
4. Press `Patient Tests` — the booked tests are there; add anything extra the patient asked for — then `F10`.

**What you should see**`An ordinary visit with a new `Acc. No.`, and not one character of the patient's data re-typed.

[FIGURE]  [IMAGE img/img-013.webp — بيانات الحجز منزّلة في شاشة Registration]

_Caption:_ The patient "adham test", age 30, phone, address and contract — all pulled from booking 140. 01:08:45

Awaiting confirmation in booking:** the `Cancelled` box's behaviour (does it void the booking and block conversion?), the meaning of `Visit Date` and `Last Visit Date`, the look of the preliminary labels and job order (not printed in training), and whether `Comments` carry into the registration.

---

<!-- section c6 -->

## 06Batch RegistrationPartial

**

A company sent ten employees for the same periodic check. Registering them one by one in `Registration` means choosing the tests ten times. `Batch Registration` lets you type the names row under row, choose the tests **once for everyone**, and print job orders and labels for all ten in one go.

[FIGURE]  [IMAGE img/img-014.webp — شاشة Batch Registration]

_Caption:_ **`Top:** `Batch Registration Data` — `Rank`, `Relative`, `Location`, `Doctor` applied to the whole batch. **Bottom:** the `Patients` grid, one row each: `Patient No.`, `Title`, `Patient Name`, `Birth Date`, `D M Y`, `Sex`, `Patient Condition`. Then `Patient Tests` and `Patients Count`. 00:49:45

[PROCEDURE]

#### Registering a batch of patients with the same tests

**Path** `System main menu > Batch Registration`

**Before you start** Know the group's contract (`Rank`/`Relative`) — it applies to every name

`
1. In `Batch Registration Data` set `Rank` and `Relative` (the group's contract), `Location`, and `Doctor` if any. 
2. On the first `Patients` row: pick `Title` (fills `Sex`), type `Patient Name`, then the birth date or the age straight into `Y`. Press `Enter` or move to the next row and repeat for each person. `
3. Watch `Patients Count` — it counts the rows you entered. `**
4. Press `Patient Tests`. The usual test-selection screen opens; choose the tests **once** — they apply to the whole batch. `
5. Press `F10`.

**What you should seeAn accession per person, one **batch number** (`Batch No.`) grouping them, and job orders and labels for all ten printing together. If the group pays cash, settle the batch from the `Batch Payment` screen below.

[NOTE:BAD] No similar-name warning here`**`

In `Batch Registration` an existing name **registers a new patient with no prompt** — the source says so and treats it as something for the developer to fix. So if batch members are past patients, enter their numbers in `Patient No.` yourself.

### Group payment — Batch Payment

[FIGURE]  [IMAGE img/img-015.webp — شاشة Batch Payment]

_Caption:_ `Type the `Batch No.` and every member appears, one row each: `Patient Fee`, `Insurance Fee`, `Discount`, `Paid`, `Recovery`, `Remainig` (spelled so on screen), `Pay`. Above, `Pay All`: one amount settles everyone. It serves both `Batch Registration` and `Lab To Lab` batches. 00:59:40

`**`**`**The literal steps inside `Batch Payment` were not shown** — the screen did not work in the training build. The source only describes the idea: type the batch number and its members appear; settle one by one with the `Pay` column or everyone with `Pay All`. How a `Pay All` amount is split, and how the receipt prints — unconfirmed. It notes the screen is **rarely used**, since most groups are on contract rather than cash. The confirmed alternative: settle each case on its own via `Registration` → `Patient Tests` → `Money`.

---

<!-- section c7 -->

## 07Samples from contracted labs — Lab To LabPartial



A small lab contracts with you to send its samples for you to run. The menu item is `Lab. Registration`, but the window that opens is titled **`Lab To Lab Batch Registration`** — also a batch, except that instead of a contract you pick the **sending lab** and all its data drops in.

[FIGURE]  [IMAGE img/img-016.webp — شاشة Lab To Lab Batch Registration]

_Caption:_ **`Top:** `Batch Registration Data` — choosing `Lab` (1 Lab1) auto-filled `Representative` (the courier: Ahmed Mohamed), `Rank`, `Relative`, `Tel. No.`, `Doctor`, and generated `Batch No.` 20. **Middle:** `Patients` — each sample/patient's data, a `Shortcut` field to choose tests **on this same screen**, and the `Sample Collected Inside the Lab` box. **Right:** `Services` — chosen tests, prices and `Tests Count`. 00:58:30

[PROCEDURE]

#### Registering samples arriving from a contracted lab

**Path** `System main menu > Lab. Registration`

**Prerequisite** `The sending lab is defined in setup (`Setup` ▸ `General Data` ▸ `Lab`) with its contract and courier


1. In `Lab` press `F9` and choose the sending lab. Its data fills (`Representative`, `Rank`, `Relative`, `Tel. No.`, `Doctor`) and a `Batch No.` appears. 
2. In `Patients`, per sample: `Title`, `Patient Name` (+`Enter` for the similarity check), `Birth Date` or age, `Sex`. 
3. In `Shortcut`: `F9` and pick the test — it lands straight in `Services` on the right, no need to open `Patient Tests`. Repeat per test. ``
4. `Sample Collected Inside the Lab` — **this is a responsibility box**. Ticked by default, meaning your side drew the sample (in your lab or by your courier). **Untick it** if the tube arrived already drawn by the sending lab: the job order then reads `Sample Collected Outside`, and draw quality is their responsibility, not yours. 
5. Add `Comments` if needed. Press `F10` — the barcode and job order for this sample print. **`
6. The lab's data and batch number **stay at the top**; move to the next row and register the second and third samples under the same batch (the status bar counts them: `Record: 1/10`). **`
7. To start a batch **for another lab**: press `F6` (new record), then `F9` in `Lab` and pick the next lab.

**What you should see**`An accession per sample under the batch number. In training the case later showed in `Registration` as 1786 with an empty `Location`. Settlement goes through `Batch Payment` by batch number.

[NOTE:INFO] The courier — Lab Representative

The person who collects samples from contracted labs and brings them to you. Defined in the lab's setup, and a report is built on it: how many labs they serve, how many samples they brought in a period, and their share. Choosing the lab fills it automatically, so don't type it.

[NOTE:TIP] A sending lab's patient shows in Registration too`

Anything registered through `Batch Registration` or `Lab To Lab` can be retrieved in `Registration` as usual (`F7` / `F8`) — including the case you just saved. From there `Patient Tests` → `Money` settles one case on its own if needed, instead of the whole batch.

**`Awaiting confirmation:** the lab setup screen (`Setup` ▸ `General Data` ▸ `Lab`) was not opened; why `Patient No.` stays empty in `Payment` for patients on this path; the `Area` field; and the literal `Batch Payment` steps — the screen **did not work in the training build**, so nothing was demonstrated on it.

---

<!-- section c8 -->

## 08Results and SearchComplete

**

Once the sample is drawn and reaches the unit, work moves from reception into the lab: **entering the result** (from an analyser or by hand), **reviewing** it, **signing it off**, then **printing**. Two screens carry all of it: `Results` for one case in front of you, and `Search` for finding cases by status and working them in bulk. This chapter is written in the voice of the chemist at the screen, then of the reception clerk who prints. Every hh:mm:ss here points into the **session 6** recording.

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 8.1 | Opening a case on the Results screen (by visit, patient, name, status) | Chemist / technician |
| 8.2 | Reading the screen: header, grid, colours | Everyone |
| 8.3 | Entering a result by hand | Chemist / technician |
| 8.4 | The three kinds of comment; adding one to the report | Chemist |
| 8.5 | Review and sign-off — `Review` and `Verify` | Chemist / unit head |
| 8.6 | History and audit: `RL` · `SL` · `Last Result` · `ATT` | Chemist |
| 8.7 | Delta Check` — what to do when it flags | Chemist |
| 8.8 | Preview and print from the Results screen | Chemist / reception |
| 8.9 | `The `Search By Patient Data` screen — searching by status | Everyone |
| 8.10 | Closing an analyser's work: `From Analyzer` and `To Analyzer` | Chemist |
| 8.11 | Bulk review and sign-off from Search — and its limits | Chemist / unit head |
| 8.12 | The print list and the abbreviated report | Reception / manual-bench technician |
| 8.13 | Results By Sample Id` — from the tube to the result | Technician |

### 8.1 · Opening a case on the Results screen

[FIGURE]  [IMAGE img/img-017.webp — قائمة Results]

_Caption:_ `The `Results` menu**: `Results By Accession No.` · `Results By Sample Id` · `Reject Sample` · `Unreject Sample`. The first is for whoever holds a visit or patient number; the second for whoever holds only the tube and its barcode (procedure 8.13). Sample rejection is in chapter 09. 00:00:17

**`

The Results screen does not open on a case — it opens **in query mode**. The status bar reads `Enter-Query`, and every header field is a search criterion. The trainer's rule: "whatever you type at the top brings the data below".

[PROCEDURE]

#### Opening one case — or a set of cases by status

**Path** `Results > Results By Accession No.`

**Right** A unit user (chemist / technician). ⚠️ [Needs confirmation: the right's name in user management]

**Prerequisites** `A visit or patient number, a name, or just a status you want to pull (like everything `Complete`)

`
1. Open the screen. The cursor sits in the header and the status bar reads `Enter-Query`. `Verify` and `Review` are greyed because nothing is loaded. `
2. **Holding the visit number?** Type it into `Acc. No.` (say 846) and press `F8`. **Holding the patient number?** Type it into `Patient No.`, then `F8` — their visits load **newest first** and you step through them with the down arrow or the `Next Record` / `Previous Record` toolbar buttons. **Holding nothing?** `F8` alone fetches every record — avoid it on a big database. `
3. If the screen has already left query mode (a case is loaded), press `F7` first to clear it back to `Enter-Query`, then type your criterion, then `F8`. **
4. **By name:** type part of the name between two `%` signs in `Patient Name.` (e.g. `%test%`). In Arabic avoid letters with more than one written form: "أحمد" may be stored with or without the hamza, so type `%حمد%`. **
5. **By status:** pick `Test Status` = `Complete` (and, if you like, `From Analyzer` = `Kx21`), then `F8`. Every matching case loads in sequence and you walk through them with the navigation arrows, no `Search` screen needed.

**What you should see**  The window title carries the patient's name in brackets (`L D M - [ابتسام عامر شاذلي عبد الله]`), the header is filled with the visit's data, the grid shows the profile and its tests, and the status bar reads `Record: 1/22` — 22 rows in this visit. `Verify` and `Review` are now enabled. 00:12:30

[NOTE:INFO] What the grid gathers under one visit `

**Every record on this screen = one independent report that will print.** Tests are grouped by `Clinical Group` to print together, except a `Mega Profile`, which prints as a separate report even with a single test (chapter 03 §8). So the screen shows together every test of the **same unit and same group** in the visit, even across different tubes, and what you see is exactly what prints. So one grid can hold `Received` rows next to `Ordered` ones: ALT and AST on one sample, albumin on another because its condition differs; if only one barcode was scanned, its tests show received and the rest stay as they were. And a test's status is independent of its sample's — in one profile a test may be `Complete` while another is not. 00:27:22

### 8.2 · Reading the screen — what you are looking at

[FIGURE]  [IMAGE img/img-018.webp — شاشة Results بحالة CBC مكتملة]

_Caption:_ A loaded case** (accession 846, unit `HAEMATOLOGY`, analyser `Kx21`). The profile name `CBC` heads the grid, then its test rows. `From`/`To` are the reference range, and every value outside it is shaded per the legend at the bottom: pink = `High`. Note the red-framed `RL` on the `R.D.W.` row alone — its result was edited (procedure 8.6). 00:12:30

`

#### The header — `Patient data`

| Fields | What they do |
| --- | --- |
| `Branch` · `Unit` · `Group` | Lists narrowing the search to a branch, unit and group |
| `Acc. No.` · `Patient No.` · `Patient Name.` · `Invoice No.` · `B. No.` | Keys to reach one specific case |
| `Test Status` ▾ · `Sample Status` ▾ | `Filter by status — e.g. `Complete` to fetch what has results entered |
| `Not Printed` ☐ · `Not Verified` ☐ · `Delta Check` ☐ | Filters: not printed · not verified · flagged by the delta check (procedure 8.7) |
| `From Analyzer` · `To Analyzer` | What came from a given analyser, and what is meant to run on one (detail in 8.10) |
| `AgeD M Y` · `Sex` · `Loc. Type` · `Location` · `Room No.` · `Doctor` · `Reg. Date` · `Tel. No.` | Patient data from registration — for display and filtering. Sex and age are what set the reference range in the grid |
| `Test` · `Asc Sort By` ▾ | One specific test, and the display order |
| `CR` | **`The registration comment**: what reception typed into `Comments` when registering the visit (a medication, a note useful to the chemist). When one exists the button shows a **red frame** — read it before judging the result |
| `Comments` | **An internal note** between unit staff ("this sample will be late because…", a question about the patient). Saved with user and time, **never printed**, and the button is framed when one exists |
| `Image` · `Biohazard` · `Controls` | Show an image attached to the visit (if attaching is enabled at registration) · the biohazard flag from registration · heading of the QC-controls group ⚠️ |
| `Preparations` · `Diagnosis` | Show the preparation questions asked at registration and the chosen diagnosis — enabled only when data exists (disabled in training) |
| `Review` · `Verify` · `R` | `Action buttons on the loaded case (8.5). ⚠️ [Needs confirmation: what `R` does in this header — on the Search screen it opens the result] |

`

#### The `Results` grid

| Column | Meaning |
| --- | --- |
| `Test Name` | `The test. The profile name (`CBC`) appears as a banner across the grid above its rows |
| `Result` | **`The field has **three natures** set by the test's setup: **typeable** (a number or a word), a **value list** opened with `F9` (`Negative`/`Positive`…), or a **formula** computed from other tests that **cannot be typed into**. An empty cell shows "." |
| `Unit` | The unit of measure (`x10^6/uL`, `%`, `fl`, `pg`…) |
| `From` · `To` | **`**The numeric reference range** — it changes with sex, age and patient condition: for a 27-year-old woman `RBCs` 3.8–5.2, for a 52-year-old man 4.4–5.9. Not edited here — edited in setup (some builds allow it, which is non-standard) |
| `N.C. Ranges` | **`**The non-numeric reference range**: text such as `Negative` for descriptive tests. Numeric ranges live in `From`/`To`, non-numeric ones here |
| `Analyzer` | `The analyser that sent the value (`Kx21`). Manual or calculated rows show none |
| `Titer` · `Methodology` | Two customisable columns, not in every build: some labs use them as "Result 1 / Result 2" to keep the pre-edit value visible to the doctor |
| `Sample Status` · `Test Status` | The two statuses of chapter 03 per row: `Received` / `Complete` on an entered case, `Received` / `Ordered` on one awaiting entry |
| `P` · `Printed` ☐ · `Delta Check` ☐ · `C` | `Per-row flags: printed · delta-check raised. ⚠️ [Needs confirmation: `P` (said to be "Printed") and `C` (probably Control — see the `Test / Analyzer` dialog below)] |
| `ATT` · `RL` · `SL` | `**`Three buttons per row: the result's **attachment** · the result's **edit log** (`Result Log`) · the **status log** (`Status Log`). Detail in 8.6 |



**Below the grid:** a `Comment` area and the buttons `Insert Comment` · `Print` · `Preview` · `Last Result`, plus the result colour key: High · Low · High Panic · Low Panic. `High`/`Low` = out of range, pay attention. `Panic` = **a critical value that must be reported at once**. An all-white grid = nothing to stop for. 00:39:56

[FIGURE]  [IMAGE img/img-019.webp — شاشة Results بحالة بلا نتائج]

_Caption:_ A case awaiting entry** (accession 1783 — the very cases registered in chapter 04): `Result` empty, `Test Status` = `Ordered`, `Sample Status` = `Received` (via `Auto Receive`) — so entry is allowed. The reference ranges are ready before any value, and for a 52-year-old man they differ from the woman in the previous picture. 00:51:40

[FIGURE]  [IMAGE img/img-020.webp — حوار Test / Analyzer]

_Caption:_ `**`**`Test / Analyzer`**: `Test` · `Analyzer` · a `Control` list · `Back`. In training: "6-No control defined" — no QC controls defined for `RBCs` on `Kx21`. ⚠️ [Needs confirmation: which button opens it — most likely the `C` column]. 00:21:30

### 8.3 · Entering a result by hand

`**

Results arrive two ways: from an interfaced analyser (landing as `Complete`, ready for review — procedure 8.10), or **by hand** from a technician reading an uninterfaced instrument or a slide and typing. This procedure is the second way.

[PROCEDURE]

#### Writing the results of a case awaiting entry

**Path** `Results > Results By Accession No.`

**Prerequisites** The sample is `Received` at the unit (scanned, or `Auto Receive` on) and the test is `Ordered`, not `Reserved` — the rules of chapter 03


1. Open the case (procedure 8.1). Check the row's `Sample Status` is `Received` — otherwise the result will not be accepted. `**
2. Stand in the first test's `Result` cell. **Typeable field?** Type the value (5.20) and move with `Tab` or `Enter` to the next row. **Field with a list?** (the status bar reads `List of Values`) press `F9` and pick `Negative`, `Positive` or whatever was set up. **Field refuses typing?** It is a formula — leave it, it is computed from the tests it depends on. `
3. Watch the colour as you type: a value outside `From`–`To` turns pink or orange, and a critical one takes the `Panic` colour. That is your first cue to re-check the reading. `
4. If `CR` is red-framed, press it and read reception's comment before going on — it may explain an odd value (medication, fasting…). `
5. Add a report comment if needed via `Insert Comment` (procedure 8.4). `
6. Save with `F10`.

**What you should see** ` The values sit in place with their colours, and `Test Status` on the written rows is now `Complete` — "written, awaiting review and sign-off". Nothing is verified yet and nothing will print. ⚠️ [Needs confirmation: that the move to `Complete` happens on save rather than by a separate button — training explained the field types without typing on camera]

[NOTE:BAD] "The test won't take a value" — don't raise a ticket before asking `

The engineer's first question on this complaint: is the test a formula? A formula is computed from other tests registered with it and is never typed into. Second question: is the sample `Received`? 00:20:30

### 8.4 · The three comments — and which one reaches the patient

| Where | Who writes it | Printed? |
| --- | --- | --- |
| `Comment(bottom of screen) + `Insert Comment` | The chemist, about the result | Yes** — it appears on the patient's report |
| `CR` | Reception, at registration (the `Comments` field in `Registration`) | No — read only; a red frame = a comment exists |
| `Comments` (header button) | Any unit member, for colleagues | No — internal, with author and time |

[PROCEDURE]

#### Adding a report comment from the unit's library

**Why a library** `Uniform, correctly spelled comments instead of each technician's own wording. Defined in `Setup` per unit/group (session 9) 00:36:36

`
1. With the case loaded, press `Insert Comment`. The `Comments` dialog opens and the status bar reads `Choices in list: 11` — your unit's number of templates. 
2. Type part of a title between `%` into `Find` (e.g. `%RBC%`) and press `Find` — or browse the list: `Comment Title` on the left, the full `Comment Text` on the right. `
3. Select the template (`RBCs 2`) and press `OK`. Its text lands in the `Comment` area: "- RBCs show mild microcytic hypochromic anemia." Edit or extend it freely if needed. `
4. Save with `F10`.

**What you should see**`The comment in the `Comment` area; it will print under this visit's results on the report. 00:36:33

[FIGURE]  [IMAGE img/img-021.webp — حوار Comments]

_Caption:_ ``Comments`**: `Find` (accepts `%`), a `Comment Title` / `Comment Text` list — `B-Thalassaemia`, `CBC 1` ("WBCs and Platelets are normal."), `CBC 2` ("Normal Blood Picture."), `Hb`, `Platelets 1/2`, `RBCs 1/2`, `WBCs 1/2`… — and `Find` · `OK` · `Cancel`. 00:36:50

[NOTE:INFO] Automatic comments `

The `Auto Verification` module can insert a comment by rule ("if the result is X, comment Y"). A wrong rule yields a wrong comment — exactly like picking the wrong template by hand. Not part of the standard Results screen. 00:38:25



### 8.5 · Review and sign-off — `Review` and `Verify`

The order in this version: `Ordered` → `Complete` → `Review` → `Verify` (chapter 03). The trainer noted that the newer web edition swaps review and verification and considers that more correct, but this guide documents what is on your screen. 00:25:00

[PROCEDURE]

#### Reviewing and signing off one case from the Results screen

**PrerequisitesThe result is `Complete`. An `Ordered` test (no result) **cannot be verifiedWho may** `Review for the chemist; sign-off for whoever holds the right (usually the unit head). The two rights are `R` (Review) and `V` (Verify) per medical group in `Groups And Privileges` — chapter 12.


1. Open the case and check every row: the value against `From`–`To`, the colours, `Delta Check` if ticked (procedure 8.7), and `Last Result` to compare previous results. `
2. Press `Review`. The case's tests move to `Reviewed` and your name and time are written into `SL`. 
3. Whoever holds sign-off presses `Verify`. The tests become `Verified` — which is what allows printing. **
4. **An allowed shortcut:** the unit head may skip `Review` and press `Verify` directly on a `Complete` result; one may even write the result and `Verify` at once. The only condition is that the result is written. 00:50:59

**What you should see**Test Status` on the rows reads `Verified`; on the Search screen the case turns into a black row (`Verified & Not Printed`) ready for reception.

[NOTE:WARN] Sign-off is a signature 

`SL` records who verified and when. Whoever presses `Verify` on a result they have not looked at signs what they did not read — and the trainer was blunt: "this isn't people's interests, it's people's lives". 00:53:03



### 8.6 · History and audit: `RL` · `SL` · `Last Result` · `ATT`

| Button | What it opens | When you need it |
| --- | --- | --- |
| `RL(tooltip `Result Log`) | `The window `Results log for Acc No: 846 Test: R.D.W.`: columns `Result` · `Date` · `User` · `Analyzer` ☑ — every value this test ever held, who wrote it, and whether it came from the analyser | **`The button shows a **red frame** on the row whose result was edited. In training: 43.9 from `Kx21` on 03-12-2015, then 14.0 by Dr Mohamed Farouk on 07-12-2015. 00:33:00 |
| `SL(tooltip `Status Log`) | `The `Status Log` window: `Status` · `Modified by` · `Modify Date` — the sample's and test's journey together: `Collected` → `Received` → `Ordered` → `Complete` → `Reviewed` → `Verified` → `Printed` (and reprints) | When asked: who received it? when was it completed? who verified? how many times was it printed? 00:35:18 |
| `Last Result` | This patient's previous results** for the same tests. The red marker beside it = previous results exist | `Before sign-off, and whenever `Delta Check` is ticked: Hb was 16.2 and is now 10.6 — was there bleeding? 00:37:52 |
| `ATT` | `The `Result Attachment` dialog to attach a document (PDF) to this row's result | An external instrument report, a scanned slide image, a reference-lab report |

[FIGURE]  [IMAGE img/img-022.webp — Results Log]

_Caption:_ ``Results Log`** for `R.D.W.` on accession 846: the original analyser value (☑ `Analyzer`), then the manual edit under its author's name. 00:33:00

[FIGURE]  [IMAGE img/img-023.webp — Status Log]

_Caption:_ `**`**`Status Log`**: `Collected` and `Received` by `ADMINISTRATOR` at 13:20, `Ordered` by another user at 13:34 (the test was added after receipt), `Complete` by the chemist six days later. 00:35:18

[PROCEDURE]

#### Attaching a PDF to a result — as written on the dialog itself

`
1. Press `ATT` on the test's row. `Result Attachment` opens with a `Result Document Selection` area and the instructions printed on it. **`
2. **Right-click** inside the area → `Insert Object...` → choose "Adobe Acrobat Document" from the type list → pick the PDF from your PC. It opens in Acrobat Reader. `
3. Press `Save` on the dialog. 
4. **To view later:** double-click the area. **To remove:** `Delete`, then `Save`.

**What you should see**The document inside the area. Remember attachments do not transfer between branches by default (chapter 18). 00:32:47

[FIGURE]  [IMAGE img/img-024.webp — Result Attachment]

_Caption:_ ``Result Attachment`** — the three instruction blocks (insert, display, delete) are printed on the dialog, with `Save` · `Delete`. 00:32:47

`

### 8.7 · `Delta Check` — what to do when it flags

`

**What it is:** a comparison of the current result with **the same patient's previous results within a set period**. If it moved by more than the percentage configured for that test (say 10%), the row's `Delta Check` box is ticked. Previous Hb 12, today 13: within the band, no flag. Today 14 or 15: flagged. Percentage and period are set per test in setup, and not every test is subject to it — some vary naturally and fast. 00:29:14

[PROCEDURE]

#### `Investigating a `Delta Check` flag before sign-off

`
1. Press `Last Result` and compare: what was the previous value and its date, and how big is the change? **
2. **The instrument first:** check the `QC` of the analyser that produced the value (`Quality Control` on the main menu — a later session). If its controls are sound, the reading itself is probably right. **
3. **The patient second:** ask reception, the doctor or the patient: bleeding? an accident? a transfusion? a new treatment? Read `CR` if it is red-framed. A convincing reason = the result is right; sign it off and note the reason in `Comment` if useful. **
4. **The sample third:** no reason in the instrument or the patient ⇒ suspect the sample itself: another patient's tube, a clot, swapped labels. Re-run from a fresh sample before verifying anything.

**What you should see**The flag does not stop you verifying — it warns, it does not reject. But it stays recorded on the row, and the lab can pull everything flagged from the header (☑ `Delta Check`, then `F8`).

### 8.8 · Preview and print from the Results screen

[PROCEDURE]

#### Previewing a visit's report, then printing it

`
1. With the case loaded, press `Preview`. The report opens in the Oracle Reports previewer exactly as it will print. 
2. If the status bar shows `There are other results have no value`, the visit still has tests without a result and the preview is refused until they are written or dealt with (set `Reserved`, say). 00:37:28 **
3. Press `Print` to print directly. **What is not `Verified` does not print** — if unverified results do print at your site, that is a setup option in your build. 01:00:26

**What you should see**`The `Printed` box is ticked on the rows, `SL` records `Printed` under your name, and on the Search screen the row turns from black (`Verified & Not Printed`) to green (`Verified & Printed`). For bulk printing from reception see 8.12.

`

### 8.9 · The `Search By Patient Data` screen — searching by status

[FIGURE]  [IMAGE img/img-025.webp — قائمة Search]

_Caption:_ `**`**The `Search` menu**: `Search By Patient Data` (the most important and most used — this procedure's subject) · `Search By Test Result` (by result value, statistical) · `Search By Birth Date` (rare). The last two were not shown in training ⚠️. 00:43:00

**

This screen's purpose differs from the Results screen: it does not open one case but **pulls every test matching a state** — everything completed but unprinted, everything unverified, everything from a given analyser — and lets you work them from one place. The trainer called it "the statistics screen"; the chemist closes an analyser's work with it, reception prints the day's results with it.

[FIGURE]  [IMAGE img/img-026.webp — شاشة Search By Patient Data فارغة]

_Caption:_ As it opens.** `Branch` is yours, `Date From` today 00:00 and `Date To` tomorrow 00:00 (one full day), ◉ `All`, and ☑ `Received` pre-ticked. `Count` 0, and the print-list buttons stay disabled until results appear. 00:43:25

[PROCEDURE]

#### Running a search and working its result

**Path** `Search > Search By Patient Data`

**Golden rule** `An "empty" result is, 90% of the time, one of two things: the default date range (today only) or ☑ `Received` (the sample is not received yet)

**`
1. **Set the date range.** The default is one day — the standard for daily work. For older cases change `Date From` (in training the year was changed from 2022 to 2019 and the cases appeared). 00:48:50 **`
2. **Narrow the place if you like:** `Unit` ▾, `Group` ▾, `Location`, or `Name` / `Doctor` / `Test` (orange, `F9`), or an `Acc. No. From`/`To` or `S ID From`/`To` range. **`**`_
3. **Pick the status** from the boxes — and understand their logic: `Complete` / `Reviewed` / `Verified` / `Printed` fetch that status alone. But **`Not X` fetches everything below X**: `Not Verified` = `Ordered` + `Complete` + `Reviewed`; `Not Reviewed` = `Ordered` + `Complete`; `Not Complete` = `Ordered` only. `All Verified` / `All Reviewed` = visits where _every_ test is so. 00:53:09 `
4. With ☑ `Not Complete` the two `NC` options enable: ◯ `All N.C.` visits with every test missing, ◯ `Mixed` visits with some done and some missing — pick `Mixed` to finish the stragglers and close whole visits. 00:53:59 `
5. **☑ `Received`:** keep it for results work (everything you review is received). **Untick it** when tracking a sample that "isn't there" — it is most likely not received yet. Many stumble here. 00:55:10 `
6. Optional: ◯ `Normal` / `Abnormal` to split normal from the rest, ◯ `In Patient` / `Out Patient` (tooltip `Location Type`), ☐ `Manual` for bench tests, `Sort By` ▾ (`Acc No` · `Patient Name` · `Registration Date` · `Location`). `
7. Press `Search`. Read `Count`, then **each row's colour** (the key below the grid), and the `Status` beside `Count`, which describes **the selected row** in a sentence like `Incomplete & Not Printed & Sample: Received`. 
8. To open one row's result press its `R` button — the Results screen opens on that case. To start over press `New Search` (clears the results, keeps the filters). **`
9. **To accumulate several searches into one list** tick ☑ `Keep Previous Results` before the second search: the first's results stay and the second's are added instead of wiping them. 00:56:01

**What you should see**In training: `To Analyzer` = `Kx21` + ☑ `Not Printed` + `Date From` 2019 ⇒ `Count` 3: visits 1783, 1784, 1785 — all `CBC` / `HAEMATOLOGY` / `TypeM`, in pink = `Incomplete & Not Printed`. 00:49:13

[NOTE:INFO] Buttons here, not keys — and a record is a report `

This screen is the second kind in chapter 03 §7: searching is done **only** with `Search` and `New Search`, and `F7`/`F8` are disabled on it. Every result row is **a report** meeting your criteria — `Count` is the number of reports, not of tests — and one row may hold several `Sample ID`s and tests; the `R` button opens that very report with all its samples.

[FIGURE]  [IMAGE img/img-027.webp — Search By Patient Data — Count 3]

_Caption:_ ``Count` 3.** Row 1783 is selected and the sentence above the grid describes it. Grid columns: `Acc. No.` · `Patient Name` · `Type` · `Group` · `Test Name` · `Internal Code` · `Sample ID` · `R`. 00:49:13

[FIGURE]  [IMAGE img/img-028.webp — مفتاح ألوان الحالات]

_Caption:_ **`_**Nine states, nine colours:** `Incomplete & Printed` (dark blue) · `Incomplete & Not Printed` (light pink) · `Completed & Printed` (purple) · `Completed & Not Printed` (red) · `Verified & Printed` (green) · `Verified & Not Printed` (black) · `Reviewed & Printed` (pink) · `Reviewed & Not Printed` (light green) · `Reserved & Not Printed` (yellow). Colours are per-installation — the _meaning_ is what stays. 00:43:25

[NOTE:INFO] The `Type` column: T and M — and `Internal Code`

Single tests (`Ferritin`, `AST (SGOT)`) carry `T` and an `Internal Code` like `C22/2846`; packages (`CBC`, `Urine Analysis`) carry `M` with no code. ⚠️ [Needs confirmation: what the letters expand to (Test / Multi?) and the internal-code rule — neither was said aloud.]



### 8.10 · Closing an analyser's work: `From Analyzer` and `To Analyzer

Everything an interfaced analyser sends lands as `Complete` — it never becomes `Verified` by itself unless the `Auto Verification` module is set up with rules. Without it, the chemist must go through what the analyser sent, review and verify it. These two fields exist for that: 00:44:41

| Field | Meaning | Typical use |
| --- | --- | --- |
| `From Analyzer` | **Results that **came from** this analyser | `☑ `Complete` + `From Analyzer` = `Kx21` ⇒ everything the analyser produced that awaits your review |
| `To Analyzer` | **Tests **meant to run** on this analyser per setup | `☑ `Not Complete` + `To Analyzer` = `Kx21` ⇒ what the analyser has not produced yet: a sample never loaded, never read, never sent |

[PROCEDURE]

#### Closing an analyser's work at the end of a shift

`
1. `Search By Patient Data`: ☑ `Complete`, `From Analyzer` = `Kx21` (`F9`), today's range ⇒ `Search`. These are the analyser's results waiting for you (red rows = `Completed & Not Printed`). 
2. Open each row with `R`, check the values, colours and `Delta Check`, then `Review` (and `Verify` if you hold the right) on the Results screen, and come back for the next row. `
3. Then `New Search`: ☑ `Not Complete`, `To Analyzer` = `Kx21` ⇒ `Search`. What appears is the leftover that should have come off the analyser and did not: trace why (sample not received? never loaded? not read?).

**What you should see**`The second search returns `Count` 0 — the analyser is "closed": no unreviewed result and no pending sample whose reason is unknown. 00:47:03

### 8.11 · Bulk review and sign-off from Search — and its limits

`**

Bottom right of the Search screen sit three buttons: `Test Result` · `Review` · `Verify`. The last two apply the action **to every listed result at once**. The trainer answered "right or wrong?" plainly: right as a tool, wrong if pressed on results you have not looked at. 00:50:14

[PROCEDURE]

#### Safe bulk sign-off

**
1. Search with a criterion that makes the list **homogeneous**: ☑ `Complete` + ◉ `Normal` (+ the analyser or group). Normal results only. `
2. Pass over the rows with `R` — even quickly — that is what makes the next step legitimate. 
3. Press `Review` (whole list), then `Verify` (whole list) if you hold the right. `**
4. Search again with ◉ `Abnormal` and handle each case **individually** on the Results screen (procedures 8.5 and 8.7).

**What you should see**The rows turn from red (`Completed & Not Printed`) to black (`Verified & Not Printed`) — ready for reception. ⚠️ [Needs confirmation: whether the buttons act on the whole list or only selected rows — the audio says "all of them at once".]



### 8.12 · The print list and the abbreviated report — `Print List` and `Abbreviated Report

The right panel of the Search screen is a print list: `Add Acc. No.` · `Print List No.` · a grid of `Acc. No.` / `Patient Name` / `Test Name` / `Sample ID`, and below it `Add All to Print List` · `Abbreviated Report` · `Clear All`. For reception it is how the day gets printed in bulk; for the bench technician it is a worksheet.

[PROCEDURE]

#### Reception: printing everything verified and not yet printed

`
1. ☑ `Verified` + ☑ `Not Printed` (+ today's range) ⇒ `Search`. The black rows are what awaits printing. 00:48:33 **
2. Put them on the list: **double-click** a row to move it alone to the right, or `Add All to Print List` to move them all. A visit that did not show in the search? Type its number into `Add Acc. No.` (in training 1782, 1783, 1785). 00:49:17 `
3. Check the list; `Clear All` and start again if needed. **
4. Print. **A rule that never changes:** what is not `Verified` does not print — if a visit did not come out, it was most likely not verified yet. ⚠️ [Needs confirmation: the list's official print button — in training the list was printed via `Abbreviated Report`] 01:00:26

**What you should see**`On the first print/report the list gets a number in `Print List No.` (705 in training), echoed in the report header as `Print List: 705`. Printed rows turn green (`Verified & Printed`). 00:59:04

[FIGURE]  [IMAGE img/img-029.webp — Print List 705]

_Caption:_ A print list being built.** Left: `Count` 8 (a search with ☑ `Keep Previous Results` over 2019→2022) — two black verified rows (1782 and 1783 `Ferritin`) and six pink incomplete ones. Right: `Print List No.` 705 holding 1782 and 1783 Ferritin, then 1785 AST and 1785 CBC added. 00:59:53

[PROCEDURE]

#### `The bench technician: `Abbreviated Report` as a worksheet for uninterfaced tests

**Why** Instead of scribbling "accession so-and-so, name, result" on scraps, the technician prints an organised sheet with every patient and their requested tests in empty boxes, fills it at the uninterfaced instrument, then enters it into the system. 00:57:50

`
1. ☑ `Not Complete` + ☐ `Manual` (+ the manual-chemistry `Group`) ⇒ `Search` ⇒ `Add All to Print List`. `
2. Press `Abbreviated Report`. The `ABBR: Previewer` opens with the report: a header with the date range, `Print List` and "Copy for file use only", then per patient a box (`Name` · `Acc. No.` · `Reg. Date` · `Location` · `Patient No.`), the `Group`, and each test with its result box and reference range.
3. Print from the previewer, take the sheet to the instrument, write the results in their boxes, then enter them with procedure 8.3 — you or a data-entry clerk.

**What you should see**`**For cases that have results the values print inside the boxes: `Ferritin` 207.0 [20 - 200] for visit 1782 and 70.0 for 1783. This report is **a file copy / worksheet, not the patient's official report**. 01:00:46

[FIGURE]  [IMAGE img/img-030.webp — Abbreviated Report]

_Caption:_ ``Abbreviated Report`** in the Oracle Reports previewer — two visits, each with its data box, group and test with value and range. 01:00:46

`

### 8.13 · `Results By Sample Id` — from the tube to the result

**`

The sample number on a tube does not reveal the visit number. So for whoever holds only the tube there is a second screen: the same grid, but **each record is one sample (tube)** with its tests, not a group. It shows the tests listed under the given `Sample ID`, and only those (chapter 03 §8). 00:41:04

[PROCEDURE]

#### Opening a tube's tests by its barcode

**Path** `Results > Results By Sample Id`
1. The screen opens in query mode. Stand in `Sample ID` and scan the barcode (or type the number 15120099229), then `F8`. 
2. Only this tube's tests appear, with its `Sample Type` (`Citrated Plasma`) and `Unit` (`COAGULATION`). Enter, review or verify with the same buttons as procedures 8.3–8.8. `_
3. Searched by visit in `Acc. No.` instead of by sample? _All_ the visit's samples load record by record (`Record: 5/6` = six tubes) and you step through them with the arrows. By `Patient No.` the patient's whole history comes.

**What you should see**In training: visit 846, record 5/6, a `Citrated Plasma` tube with `Patient.Time` · `Control Time` (11.8) · `Concentration` · `INR` · `Partial Thromboplastin Time` [26–45] — all `Received` / `Ordered`. 00:42:33

[FIGURE]  [IMAGE img/img-031.webp — شاشة Results By Sample Id]

_Caption:_ ``Results By Sample Id`**: the header adds `Sample ID`, `Sample Type` and `Internal Code`; the grid is the same (`N.C. Range`, singular, here). 00:42:33

### Worked scenarios — with the training numbers

#### 1 · The analyser finished a CBC — the chemist reviews and signs off

`Visit 846, a 27-year-old woman, a `CBC` from `Kx21` arrived `Complete`.


1. `Results By Accession No.`, `Acc. No.` 846, `F8`. `Record: 1/22`. 
2. Pink on `Hemoglobin` 16.2 [11.5–15.5], `Haematocrit` 48.9, `Platelet Count` 542 [150–440], `WBC` 22.0 [3.6–11], `Neutrophils#` 16720. No `Panic`. 
3. `RL` red on `R.D.W.`: it was 43.9 from the analyser and was edited to 14.0 by a colleague — understood, the instrument reading was aberrant. 
4. `Last Result`: no conflicting earlier results. `CR` unframed — no reception comment. `
5. `Insert Comment` → `%WBC%` → the `WBC` template → `OK`. `F10`. `Review`. Unit head: `Verify`.

**Outcome**Rows `Verified`; on the Search screen the visit is a black row awaiting reception; `SL` carries the reviewer's and verifier's names.

#### 2 · Reception prints the day's results

`End of day; visits 1782 and 1783 (`Ferritin`) are verified and unprinted.


1. `Search By Patient Data`: ☑ `Verified` ☑ `Not Printed`, a suitable range, `Search`. Two black rows. 
2. `Add All to Print List`. The doctor also asked for visit 1785 urgently: `Add Acc. No.` 1785. 
3. Print. `Print List No.` becomes 705. `Ferritin` 207.0 [20–200] for 1782 and 70.0 for 1783. `
4. 1785 did not come out — its row is pink: `Incomplete`. Its results are not written yet, so it will not print however often it is added. Tell the unit.

**Outcome**Two reports in the two patients' hands, their rows green, and `SL` records `Printed` under the clerk's name.

#### `3 · A `Delta Check` flag on haemoglobin

`A patient with a CBC a month ago: Hb 16.2. Today the analyser says 10.6. The gap exceeds the configured band, so `Delta Check` is ticked.

`
1. `Last Result`: 16.2 with its date. The gap is real and large. `
2. Today's analyser `QC` is fine. So the reading is right for what is in the tube. `
3. Ask: bleeding? surgery? The patient says nothing happened. `CR` unframed.
4. So suspect the sample: another patient's tube or swapped labels. Do not verify. Request a fresh draw and re-run.

**Outcome**`The new result is 15.8 — it was a swapped sample. `RL` keeps both values under your name, and the flag did its job: "it gives you an alarm that something is really wrong". 00:30:38

#### 4 · "The sample isn't in Search"

A sample-tracking technician looks for a tube that reached him and finds it neither in `Search By Patient Data` nor in `Worklist`.

`
1. In `Search`: untick ☑ `Received` and search again — the tube appears as `Collected`: not received at the unit yet. 00:55:10 
2. And missing from `Worklist` while present in `Search`? Because `Search` filters by registration date and `Worklist` by collection date — a registered visit not yet drawn, or drawn on another day. 01:03:53 `
3. Scan the tube at the unit (`Received`), then search again with the defaults.

**Outcome**`The tube is listed, `Received`, and ready for entry.

### Questions unit staff ask

**I opened the Results screen and it's empty — where are the cases?**

`

It opens in query mode (`Enter-Query`). Type the visit number or any criterion, then `F8`; or `F8` alone for everything.

**The analyser sent the results — why aren't they Verified?**



What comes from an analyser lands `Complete`. Sign-off is human unless `Auto Verification` is set up with rules.

**The Analyzer column is empty on some rows**



Those are manual or formula rows (`Bands%`, `Monocytes%`…). The analyser's name appears only on what it sent.

**What's the difference between Comment, CR and Comments?**

Comment` is for the report and prints. `CR` is what reception typed at registration (read it, don't edit it). `Comments` is an internal note between colleagues, never printed. A red frame on the button = there is something to read.

**RL is red on one row — what does that mean?**

That row's result was edited after it was written (or after the analyser sent it). Press it to see every value, who changed it and when.

**Can I verify without reviewing first?**



Yes, if you hold the right and the result is `Complete`. What is not allowed: verifying an `Ordered` test with no result, or verifying what you have not looked at.

**Preview refused with "There are other results have no value"**

`

The visit has tests without a result. Complete them, or set what won't be done to `Reserved`, then preview again.

**The visit is on the print list but won't print**

`

Most likely it isn't `Verified` — look at its row colour. Unverified doesn't print (except by a non-standard setup option).

**I can't find the sample in Search at all**



Untick ☑ `Received` and widen `Date From`. Most likely it isn't received, or it's from another day.

**The case shows in Search but not in Worklist**



`Search` filters by registration date, `Worklist` by collection date. It was registered but not drawn yet, or drawn on another day.

**Is the Abbreviated Report the patient's report?**

`

No. It says "Copy for file use only". It's a multi-patient file copy / worksheet. The patient's report prints with `Print`.

**What exactly does Keep Previous Results do?**

It stops a new search from wiping the previous one's results, so several searches accumulate into one list.

**The reference range is wrong — do I fix it here?**

`

No. Ranges come from test setup (session 9). First check sex, age and `Patient Condition` at registration — they select the range.

What needs confirmation in this chapter** (also collected in chapter 21): the names of the entry, review and verify rights · what the `R` button in the Results header does · the meaning of the `P` and `C` columns · which button opens `Test / Analyzer` · whether the move to `Complete` happens on save · the `R. Date` field on the Search screen · the `Search By Test Result` and `Search By Birth Date` screens · the `Image` button's behaviour · the setup option that allows printing unverified results · whether `Review`/`Verify` in Search act on the whole list or the selection · the print list's official print button · what `T`/`M` expand to and the `Internal Code` rule. Sample rejection and reinstatement are in chapter 09.

---

<!-- section c9 -->

## 09Worklist and sample rejectionComplete

**

The `Search` screen answers "which reports are waiting?"; `Worklist` answers the unit's daily question: **which tests must I do now, and which are late or about to be?** With it come two small screens that close a sample's cycle when it fails: `Reject Sample` and `Unreject Sample`. This chapter comes from session 7 (the part recorded on the training environment, 00:00–11:51); every timestamp points into it.

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 9.1 | Running and reading the worklist | Chemist / technician |
| 9.2 | `Delay: `Delayed` and the row colours | Unit head |
| 9.3 | `Tracking a sample: the `S. T.` button | Everyone |
| 9.4 | `Rejecting a sample — `Reject Sample` | Receiving technician / chemist |
| 9.5 | `Reinstating a rejected sample — `Unreject Sample` and the barcode policy | Receiving technician |

### 9.1 · Running and reading the worklist

**`

Recall the rule of chapter 03 §8: on this screen **one record = one test**. Ten rows = ten tests, even if they belong to only two visits. And it is a button screen (chapter 03 §7): search with the `Search` button, no `F7`/`F8` here.

[FIGURE]  [IMAGE img/img-032.webp — شاشة Worklist فارغة]

_Caption:_ **`**As it opens.** Filters at the top, status boxes on the right, the counters `Req: 0, Tests: 0` / `Samples: 0`, and an empty `Search Result` grid. `Date From` today and `Date To` tomorrow, `Sort By` = `Result Date`. 00:01:00

[PROCEDURE]

#### Producing the unit's worklist

**Path** `Worklist` (main-menu item)

**`The key difference from `Search``The date range here is on the **collection date** (`Collection Date`), not the registration date — so a visit can show on one screen and not the other (chapter 08, scenario 4) 00:00:03


1. Set the range: `Date From` / `Date To` (default today). Then narrow as you wish: `Unit` ▾ and `Group` ▾ for your unit, or `Test` (`F9`) for one test, or `Analyzer`, `Location`, `Patient No.` / `Acc. No. From`–`To` / `Sample ID From`–`To` / `Int. Code From`–`To`, or `Sample Status` ▾. `Rank`/`Relative` for one contract (explained in finance). 00:01:09 **
2. Tick what you want to see: ☐ `New Tests` · ☐ `Remainig` (remaining — spelled as on screen) · ☐ `Completed` · ☐ `Manual` (tests **not measured on an analyser**) · ☐ `Discounts` · ☐ `Not Verified` · ☐ `Not Printed` · ☐ `Incomplete` · ☐ `Printed` · ☐ `Delayed` (procedure 9.2). 00:02:08 
3. Optional: `Grouping` ▾ (`All`), `Sort By` ▾ (`Result Date`) ☐ `Asc`, `Auto` ▾, `Batch No.`, `Doctor`. 
4. Press `Search`. Read the counters first: `Req` the number of requests (visits), `Tests` the tests in them, `Samples` the samples — they differ because one test may need two samples. 00:01:37 `
5. Read the grid row by row: `Patient No.` · `Acc. No.` · `S ID` · `Internal Code` · `Patient Name` · `Age` · `Sex` · `Test` · `Test Status` · `Sample Status` · `Collection Date` · `Result Date` (expected) · `Delay` · `Printed` ☐ · an `S. T.` button. `
6. Press `Print` for the paper worklist report the technician takes to the bench. Colours do not print — the times do. 00:08:08

**What you should see**In training (☑ `Delayed`, `Date From` 2019): `Req: 5, Tests: 10` / `Samples: 8` — ten rows for five visits (1782–1786): Urine Analysis, CBC ×3, Lipids Profile formula, Serum Uric Acid, AST (SGOT) ×2, Ferritin ×2; eight `Ordered` and two `Verified`, all `Received`. `Record: 1/10`. 00:08:17

[FIGURE]  [IMAGE img/img-033.webp — شاشة Worklist بنتائج]

_Caption:_ **`**Ten tests, five visits.** Each row is a test with its two statuses, collection date, expected result date and the computed `Delay` (1D 20H 3Min, 2D 12H 38Min… and for the verified Ferritin 5Min and 1H 43Min). Pink = delayed. 00:08:17

`

### 9.2 · Delay: `Delayed` and the row colours

`**`

Every test carries a **`Duration`** in its setup — the time within which its result should be out. From it the system computes each row's expected `Result Date`, and from that the `Delay`. ☑ `Delayed` pulls what has passed its time. 00:02:55

[NOTE:TIP] The colours warn before the delay, not after — and by percentage, not by the hour `

The three row colours (purple and red among them) reflect **the share of the `Duration` already spent**: say 70% elapsed → first colour, 80% → second, 90% → third. Why a percentage and not a time? Because a one-month test does not care about a day's delay, while a one-hour test late by a day is a disaster. The point is that the unit head knows **what will be late before it is late**. Thresholds and colours are set per client. 00:03:17

[PROCEDURE]

#### The unit head's round on late work


1. `Worklist` → your unit in `Unit` → ☑ `Delayed` → `Search`. 
2. Look at the strongest colour first, then read `Test Status` / `Sample Status`: `Ordered`+`Received` = the sample is at the unit and the test not done; `Complete` = written, awaiting sign-off; `Verified` yet delayed = stuck at printing/reception. `
3. For puzzling rows open `S. T.` (procedure 9.3) to see where the sample stopped and since when.

**What you should see**A short list of what needs a decision now, not dozens of normal rows.

`

### 9.3 · Tracking a sample: the `S. T.` button

`

At the end of every row sits an `S. T.` (Sample Track) button. It opens a three-panel dialog answering "where was this sample and when?" 00:07:45

[FIGURE]  [IMAGE img/img-034.webp — حوار Sample Track]

_Caption:_ Three panels.** Header: `Acc. No.` 1786 · `Patient Name` · `GroupMICROBIOLOGY` · `TestUrine Analysis`. Left, `Sample Tracking`: `Sample Type` · `Location` · `Status` · `Date/Time` — "Urine Random / Auto Collect Location / `Collected` / 07-03-2022 15:53" then "… / Auto Recieve Location / `Received` / 15:53". Middle, `Sample Tracking` with durations: `Description` · `Time Period` — the time between each two stages (here 0 Min because collection and receipt were automatic). Right, `Test Round Time`: `Complete Date` · `Reviewed Date` · `Verified Date` · `First Print Date` · `Last Print Date` — empty because the test is still `Ordered`. A `Hide` button. 00:07:52

[NOTE:INFO] Three history windows — which one to open? `**`**`**S. T.` here follows **the sample across locations**, the time between its stages and the test round's dates. `SL` on the Results screen (chapter 08 §8.6) records **who** changed each status. `RL` records **result values** and their edits. "Where is the sample?" → `S. T.`; "who verified?" → `SL`; "who changed the number?" → `RL`.

`

### 9.4 · Rejecting a sample — `Reject Sample**

A haemolysed, clotted, short or wrong-tube sample is neither run nor left silently `Received`. It is rejected with a recorded reason, so reception knows to redraw and the trail stays. The screen is small and works **by sample number**, not visit — because rejection hits one specific tube. 00:08:14

[PROCEDURE]

#### Rejecting a sample with a reason

**Path** `Results > Reject Sample`

**Prerequisites** `The sample number (from the barcode), and a rejection-reason list defined in `Setup` ⚠️ [Needs confirmation: the setup screen's name — called "Sample Reject Reason" in the audio]


1. Open `Reject Sample`. The dialog appears in query mode — status bar: `Enter a query; press F8 to execute, Ctrl+q to cancel.` — with the fields `Sample ID` (red) · `Reason` · `Acc. No.` · `Patient Name`. 00:08:45 
2. Scan the tube's barcode or type its number into `Sample ID` (22030075130) and press `F8`. `Acc. No.` (1786) and `Patient Name` (sdgsgb) fill in — check they match the tube in your hand. 00:09:08 
3. Stand in `Reason` (status bar: `List of Values`), press `F9` and pick the reason — `Hemolysis`, say. 00:09:11 `
4. Press `F10`. The message `Are you sure you want to reject this sample?` appears with `OK` · `Cancel`. Press `OK`. 00:09:25
5. Tell reception or phlebotomy to redraw the sample, and state the reason.

**What you should see**The sample is now `Rejected` (the exceptional status of chapter 03 §3) and its tests take no results until a replacement arrives. The reason is stored with the sample. ⚠️ [Needs confirmation: where the reason shows later — in `S. T.` or a dedicated report]

[FIGURE]  [IMAGE img/img-035.webp — حوار Reject Sample]

_Caption:_ `**`**`Reject Sample`** after `F8`: `Sample ID` 22030075130 → `Acc. No.` 1786, `Patient Name` sdgsgb; the cursor in `Reason`. 00:09:08

[FIGURE]  [IMAGE img/img-036.webp — Are you sure you want to reject this sample?]

_Caption:_ `**`**After `F10`.** `Are you sure you want to reject this sample?` — `OK` rejects, `Cancel` backs out. 00:09:25

`

### 9.5 · Reinstating a rejected sample — `Unreject Sample` and the barcode policy

The replacement sample has arrived. Two roads lie ahead and the system supports both — the difference between them is a quality policy, not a button: 00:10:27

| Road | What you do | When |
| --- | --- | --- |
| 1 · Same barcode | Put the rejected sample's own label on the new tube, then `Unreject Sample` by its number; it returns to `Received` and its cycle resumes | The old cycle; faster, no label print |
| 2 · New barcode | `The old one stays `Rejected` with its reason, and a new sample is issued with its own label from `Registration` ▸ `Sampling Details` (chapter 04) and runs its cycle from the start | What quality standards (ISO) require**: new sample = new number. The trainer: "the proper thing is a new barcode" ⚠️ [Needs confirmation: the issuing steps after rejection] |

[PROCEDURE]

#### Reinstating the sample under the same barcode

**Path** `Results > Unreject Sample`
1. Open `Unreject Sample`. One field: `Sample ID` (red). 00:09:45 `
2. Scan the old barcode now on the new tube (22030075130) and press `F10`. `
3. A barcode-related message appears ⚠️ [Needs confirmation: its exact wording — not legible in the frame]. Press `OK`. `
4. Verify: open `Results By Sample Id` with the sample number (chapter 08 §8.13) — its tests accept results again. 00:10:05

**What you should see**`The sample has left `Rejected` and is back on its path; the rejection and its reason remain in its history.

[FIGURE]  [IMAGE img/img-037.webp — حوار Unreject Sample]

_Caption:_ `**`**`Unreject Sample`** — one field, the sample number, then `F10`. 00:09:45

### Two scenarios

#### 1 · The afternoon round on late work in chemistry

The chemistry unit head, three in the afternoon, wants to know what will not be out on time.

`
1. `Worklist` → `Unit` = CHEMISTRY → ☑ `Delayed` → `Search`. `Req: 5, Tests: 10`. 
2. The two `Ferritin` rows (1782, 1783) are `Verified` with delays of 1H 43Min and 5Min: the problem is at printing, not the unit — he tells reception. 
3. `AST (SGOT)` for visit 1784, `Ordered`/`Received`, delayed 2D 20H 38Min: the sample has sat at the unit for two days undone. `S. T.`: received 07-03 15:00 and nothing since. He asks who received it. `
4. `Print` — the paper list goes with the technician to the bench.

**Outcome**Two decisions in five minutes: a heads-up to reception and a test run at once — instead of discovering both when the patient calls.

#### 2 · A haemolysed sample — reject and redraw

`The tube for visit 1786 (`Urine Analysis`, sample 22030075130) arrived unfit.

`
1. `Results > Reject Sample` → scan the barcode → `F8` → 1786 / sdgsgb. `ReasonF9` = `Hemolysis`. `F10` → `OK`. 
2. The patient is called for a fresh sample. Lab policy: new barcode (road 2) — a new sample is issued with its label from `Sampling Details`; or, if your lab runs the old cycle, the old label goes on and `Unreject Sample` + `F10` + `OK` is done. `
3. `Results By Sample Id` with the active sample number: `Sample StatusReceived` — it gets run.

**Outcome**No result on a spoiled sample, the rejection reason kept, and the replacement on its way.

### Questions staff ask

**The case shows in Search but not in Worklist**



`Worklist` filters by collection date, `Search` by registration date. Widen the range or draw the sample.

**Why is Tests larger than Samples?**

`

Several tests on one tube, or a test needing two samples. `Req` visits, `Tests` tests, `Samples` tubes.

**The row is red and it is only 5 minutes late!**

`

The colour is a share of the test's `Duration`, not an absolute time. For a one-hour test, 5 minutes is a lot. The thresholds are set for your lab.

**F7 does nothing on this screen**

`

Right — it has a `Search` button, so the keys are disabled (chapter 03 §7).

**I want to know when and where the sample was received**

`

The `S. T.` button at the row's end: the first panel lists locations and statuses with times.

**Reject Sample asks for a Sample ID — I only have the visit number**



Rejection hits one tube. Open `Results By Sample Id` or `Samples` in registration to find the visit's sample numbers, then reject the right one.

**Does the new sample take the same barcode or a new one?**

Your lab's policy. Quality (ISO) says: new sample = new barcode. The system supports both roads (9.5).

**`_**What needs confirmation in this chapter** (also collected in chapter 21): the meaning of the `Discounts`, `Grouping` and `Auto` options on `Worklist` · the three delay colours and their default thresholds · the name of the rejection-reasons setup screen · the wording of the `Unreject Sample` message · where the rejection reason shows later · the steps to issue a new sample after rejection. _Note:_ everything after minute 11:51 of the session-7 recording (another installation over AnyDesk) is excluded from this guide by the project owner's decision.

---

<!-- section c10 -->

## 10Financial — price lists, contracts and reportsComplete

**

LDM is **not an accounting system**. What it has is a simple accounts module serving the lab in three ways: **pricing** tests per contract, **collecting**, refunding and transferring money on a visit, and the income and claim **reports** that close the day and bill the contracting bodies. Large labs and hospitals link it to an ERP that takes the income from it and does the big accounting. This chapter speaks in the voice of the accounts officer who sets prices and the reception clerk who collects, refunds and closes a shift. Every hh:mm:ss points into the **session 8** recording.

[NOTE:WARN] This session was recorded on a different installation `

The splash screen reads `LDM 9.3.0` — "Al-Barq Lab", not the `9.2.0` training build the earlier chapters were filmed on. Names and screens here come from that build; differences from 9.2.0 are small but possible (extra buttons on `Payment`, say). Report figures are test data and mean nothing. 00:02:11

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 10.1 | `A map of the `Financial` menu: setup, transactions, reports | Everyone |
| 10.2 | Pricing in three layers — the concept | Accounts / system admin |
| 10.3 | `Creating and pricing a base price list — `Main Price List` | Accounts |
| 10.4 | `Next year's list by formula — `Copy price list` | Accounts |
| 10.5 | Creating a contract: `Rank` and `Relative` — the `Price Lists` / `Rank Fees` screen | Accounts |
| 10.6 | Tying a contract to a base list by formula — `Client Price List` and `Client Copy` | Accounts |
| 10.7 | `A price enquiry before registering — `Query` | Reception |
| 10.8 | `Refunding money — `Return Money` | Reception / cashier |
| 10.9 | `Moving a visit to another contract or shifting a test's value — `Money Transfer` | Reception with the right |
| 10.10 | `Cancelling a test on a visit financially — `Delete Patient` | Reception with the right |
| 10.11 | Closing the reception day — `Income` ▸ `Total Daily Income` | Reception / management |
| 10.12 | Income, statistics and doctor reports | Management / accounts |
| 10.13 | `Billing a credit contract — `Invoice` | Accounts |
| 10.14 | A price list to hand out, costs, and the legacy screens | Accounts |

`

### 10.1 · A map of the `Financial` menu

**

In the desktop edition the menu is "unorganised", in the trainer's words: setup, transaction and report screens sit mixed in one column. This table regroups them by **who uses them and why**; names are verbatim from the menu. 00:02:11

[FIGURE]  [IMAGE img/img-038.webp — قائمة Financial]

_Caption:_ `The `Financial` menu** in 9.3.0: 22 items from `Additional Services Report` to `panel_price`, plus two submenus, `Reports ▸` and `Outsourcing ▸`. 00:03:36

| Purpose | Menu items | Where in this chapter |
| --- | --- | --- |
| Price and contract setup** (set once, updated yearly) | `Main Price List` · `Price Lists` · `Client Price List` · `Client Copy` · `Tests Costs` · `Test Fees` · `panel_price` | 10.2–10.6، 10.14 |
| Transactions on a visit** (daily at reception) | `Query` · `Payment` · `Batch Payment` · `Return Money` · `Money Transfer` · `Delete Patient` | 10.7–10.10 (و`Payment` في الفصل 04) |
| Reports and closing** | `Income` · `Patient Income` · `Patient Total` · `Doctor Income` · `Representative Income` · `Additional Services Report` · `Invoice` · `Reports ▸` (12 تقريراً) | 10.11–10.13 |
| A separate module** | `Outsourcing ▸` | Sending tests to outside labs, with its own setup and reports — "a day of its own"; outside this chapter |

### 10.2 · Pricing in three layers — the concept before the screens



Chapter 03 established that `Rank` and `Relative` at registration set every test's price. Where does that price come from? From three ordered layers, each with its own screen:

|  | Layer | Screen | What it sets |
| --- | --- | --- | --- |
| 1 | The base list | `Main Price List` | Every test's price in a named list ("2022 price list", "المؤسسة العلاجية 2017"). A lab usually keeps a yearly list plus lists for large bodies (doctors' or engineers' syndicates) |
| 2 | The contract and its tiers | `Price Lists` (the window is titled `Rank Fees`) | `Rank` = the body (Coca-Cola, Health & Wealth, Lab to Lab, Normal); `Relative` = its tiers (employees, employees' families, Cash). Each tier has its own price list split into the patient's share (`Patient`) and the body's share (`Insurance`) |
| 3 | The formula | `Client Price List` | Ties a contract tier to a base list with a factor: "take the 2022 list × 1 for the patient and × 0 for the body" = cash; "× 0.5 and × 0.5" = half and half. `Apply` computes the tier's list from the formula, and every base-list update spreads by pressing `Apply` there |

[NOTE:TIP] Why three layers and not a list per contract? 

Because a lab with twenty contracts cannot re-price a thousand tests twenty times a year. You change a price once in the base list and press `Apply`, and it spreads to every contract derived from it — except what you excluded by hand with `Exclude` (10.5). Manual pricing per contract is possible, but "a heavy burden", as the trainer put it. 00:08:05

`

### 10.3 · Creating and pricing a base price list — `Main Price List`

[FIGURE]  [IMAGE img/img-039.webp — شاشة Main price list]

_Caption:_ `The `Main price list` screen.** Top: the lists (`Code` · `Price List Name`) and the buttons `Apply` · `Print` · `Print Details` · `Copy price list`. Bottom: the selected list's prices (`Group Name` · `Test Name` · `Price` · ☑ `Active` · a per-row `Apply`). The window title carries the selected list's name ("Main price list2022") and changes with it. Status bar: `Data has been saved successfully.` 00:09:16

[PROCEDURE]

#### Creating a new list and pricing its tests

**Path** `Financial > Main Price List`

**Right** The accounts officer or system admin. ⚠️ [Needs confirmation: the right's name]

**Prerequisites** `The client's price list on paper or file; tests already defined in `Setup`
1. In the top block press the `Add New Record` toolbar icon (or move to an empty row). Type a `Code`: look at the last code used and type the next (it was 20, so the trainer typed 21). Some builds generate it automatically. 
2. Type the `Price List Name` — one that says whose list and which year ("Main price list2022", "قائمة أسعار نقابة الأطباء 2021"). `F10`. The bottom block shows empty or zeros: the list exists without prices. **`
3. **Price a whole group:** stand in the bottom block, `F7`, then `F9` in `Group Name` and pick the group (Chemistry), then `F8`. All its tests appear; type a `Price` row by row. **
4. **Or price one test:** `F7`, `F9` in `Test Name`, pick the test, `F8`, type the price (CBC = 80). **`
5. Tick ☑ `Active` on every test whose price should appear at registration. **An inactive one is invisible in `Registration`**. `F10`. 
6. Are contracts derived from this list (10.6)? Press the top `Apply` so the new prices spread to all their lists; or the row's `Apply` for one test only. `
7. `Print` prints the list as a report for review or filing.

**What you should see**The window title carries your list's name, prices sit in place with ☑ `Active`, and the status bar reads `Data has been saved successfully.` At registration, CBC shows for a patient on a contract derived from this list according to its formula (10.6). 00:05:04

[NOTE:BAD] You changed the price and the patient still sees the old one `**

Nine times out of ten: a base-list change without `Apply`. The prices registration shows are the **contract list's** prices (10.5), and they only refresh when `Apply` is pressed in `Main Price List` or `Client Price List`. The tenth time: ☐ `Active` is not ticked. 00:09:06

`

### 10.4 · Next year's list by formula — `Copy price list`

Every year the lab raises its prices, and nobody wants to retype a thousand of them. The `Copy price list` button on the `Main Price List` screen copies an old list into a new one and applies a formula to every price on the way. 00:12:20

[PROCEDURE]

#### Copying the 2021 list into 2022 with a 10% rise

`
1. Create the new list first with steps 1–2 of 10.3 (code and name, `F10`); it stays unpriced. 
2. Press `Copy price list`. Under `Old price List` pick the source list (`F9`), and under `New Price List` the one you just created. **`**
3. Set the formula: **multiply** by a factor — `Value` 1.1 = +10%, 1.2 = +20%, 0.9 = a 10% discount — or **add** a fixed amount (+10 EGP on every test, negative for a discount). The system knows multiplication is a percentage and addition an amount. 
4. Press `Apply` on the dialog. A Forms message: `The new price list has been created successfully`.
5. Open the new list and check a test or two: CBC was 80, now 88. Watch the fractions; any price can still be edited by hand after copying.

**What you should see**`A complete new list in a minute, with the same `Active` flags. Next step: point the contracts at it in `Client Price List` (10.6), then `Apply`. 00:16:09

[FIGURE]  [IMAGE img/img-040.webp — حوار Copy price list]

_Caption:_ ``Copy price list`**: `Old price List` ▸ `Price List Name` (the 2021 doctors' syndicate list) · `New Price List` ▸ `Price list Name` + `Value` · `Apply` — and the success message. 00:16:09



### 10.5 · Creating a contract — the `Price Lists` (`Rank Fees`) screen

`

The menu item is `Price Lists` and the window opens under that name, then its title becomes `Rank Fees <Rank> - <Relative>` once a contract and tier are selected. This is where the `Rank` and `Relative` that reception picks in `Registration` are born. 00:18:32

[FIGURE]  [IMAGE img/img-041.webp — شاشة Rank Fees]

_Caption:_ ``Rank Fees`.** Top left `Rank Name`: `Code` · `Rank Name` · `Web User Name` · `Web Password` · ☑ `Web Results` · a `Rank Pass` button. Top right `Relative Name`: `Code` · `Relative Name` · `Type` ▾ · `Web User Name` · `Web Password` · ☑ · a `Report` button. Bottom: the selected tier's price list — `Group Name` · `Test Name` · `Patient` · `Insurance` · ☐ `Alert` · ☐ `Exclude`, with `Restore from main price list` · `Conditions`. Note `Alert` on most rows and `Exclude` on Cow milk. 00:23:58

[PROCEDURE]

#### Creating a new contract with two tiers

**Path** `Financial > Price Lists`

**Prerequisites** The body's contract: its name, tiers, who pays what (cash/credit/percentage), and whether it gets a web results portal


1. In `Rank Name` press `Add New Record`, type a `Code` (next after the last) and the `Rank Name` (in training "test training"). 
2. Will the body view its patients' results on the web portal? Type a `Web User Name` and `Web Password` and tick ☑ `Web Results`. Tick it without a password and the system refuses: `Please, Enter Web Result Password`. 00:22:30 
3. Move to `Relative Name` and add the tiers row by row: `Code` 1 "test1", 2 "test2" (in real life: "employees", "employees' families", "Cash"). `Type` stays `Normal` ⚠️ [Needs confirmation: the other values]. Each tier may have its own web user/password if needed. **
4. `F10`. Press `Enter` on the tier and its price list drops into the lower block — **all zeros**. That is normal: it is not tied to a base list yet. Go to 10.6.

**What you should see**The window title `Rank Fees test training - test1` and the status bar `Record: 1/136` (136 tests in the tier's list). The contract and its tiers are now available under `Rank`/`Relative` on the registration screen. 00:20:00

#### The four columns that govern a contract list

| Column | Meaning |
| --- | --- |
| `Patient` · `Insurance` | `The **patient's** share (paid in cash at reception) and the **body's** share (credit, billed through `Invoice`). Cash only ⇒ `Insurance` = 0; credit only ⇒ `Patient` = 0; any split in between is possible. 00:21:13 |
| ☐ `Alert` | **Ticked on a test, the **contract's conditions** pop up for the clerk when that test is registered for a patient on this contract: "approval required", "ID copy"… The conditions themselves are written via the `Conditions` button. The trainer said "Alive"; the screen says `Alert`. 00:22:05 |
| ☐ `Exclude` | Excludes this test from the `Client Price List` formula and from `Apply`: the price typed here by hand stays and base-list updates do not overwrite it. For the test whose price the body negotiated specially. 00:23:19 |
| `Restore from main price list` | `Re-derives the tier's prices from the base list ⚠️ [Needs confirmation: whether it honours `Exclude`] |

`

### 10.6 · Tying a contract to a base list — `Client Price List`

[FIGURE]  [IMAGE img/img-042.webp — شاشة Client Price List]

_Caption:_ `**`**`Client Price List`.** Header `Rank` · `Relative`. Six boxes: `First patient price list` · `First insurance price list` · `Second …` · `Third …`, each with `Code` + base-list name · `Operator` ▾ (X) · `Value`. Bottom: `Apply Date` (filled after applying: 28/03/2022) · `Money Format` ▾ · `Apply` · `Print`. 00:28:19

[PROCEDURE]

#### Writing and applying the contract's formula

**Path** `Financial > Client Price List
1. Pick the `Rank` then the `Relative` with `F9` (test training / test2). 
2. In `First patient price list` pick the base list (21 Main price list2022), `Operator` = `X`, and `Value` = the patient's share: 1 pays everything, 0.5 half, 0.1 a tenth, 0 nothing. 
3. In `First insurance price list` the same list and `Value` = the body's share: usually the complement (0 for cash, 0.5 for half, 0.9 if the patient pays a tenth). 
4. Does the body's list include tests your base list lacks? Tie `Second …` to a supplementary list (in the Al-Barq data one is literally named "المكملة العلاجية", factor 0.7). `Third …` is a third level for rare cases. **`
5. `F10`, then **`Apply`**. `Apply Date` fills in. Without `Apply` the contract list in `Rank Fees` stays at zeros. `
6. `Print` prints the "Client Price List" report for all contracts: one row per tier saying which list and factor feed the patient's and the body's shares — an excellent yearly review.

**What you should see**`Open `Price Lists` on the same tier: the zeros are now prices (CBC 80 under `Patient` and 0 under `Insurance` for the ×1/×0 formula). Registration shows that price for the tier's patients. 00:30:46

[FIGURE]  [IMAGE img/img-043.webp — تقرير Client Price List]

_Caption:_ `**`**The `Client Price List` report** (`rank_price_list`): per `Rank Name` a table of `Relative Name` · `Primary/Secondary/Third Patient` · `Primary/Secondary/Third Insurance`. Read the Coca-Cola / employees-and-families row: patient "المؤسسة العلاجية 2017 X.9", body "X 0" ⇒ employees pay 90% cash off that list. And "Health & Wealth / employees": patient X 0, body X 1 ⇒ fully on credit; "employees' families":.1 /.9. 00:29:29

[NOTE:INFO] Client Copy` — a contract that resembles another `

The `Client Copy` screen copies one tier's price list into another with a factor: `Old Price List` (`Rank Name` · `Relative Name`) → `New Price List` (`Rank Name` · `Relative Name` · `Operator` · `Value`) → `Run`. The contract-level twin of `Copy price list`. 01:07:45

`

### 10.7 · A price enquiry before registering — `Query

A patient on the phone: "how much are a CBC and a COVID test on such-and-such contract, when is the result, and must I fast?". Don't register a visit to find out. The `Query` screen answers all three and prints a quote. 01:04:14

[FIGURE]  [IMAGE img/img-044.webp — شاشة Query]

_Caption:_ `**`**`Query`.** `Rank` Normal · `Relative` Out Patient. Grid: `Shortcut` · `Group Name` · `Test Name` · `Sample` · `Normal Patient Rank` · `Cash` · `Insurance` · `Result Date` · `Preparation`. CBC (EDTA) 143 and Covid 19 - checkup 495; total 638 cash and 0 on the body; due 29-03-2022 13:38. Buttons `Print` · `All Preparation`. 01:06:38

[PROCEDURE]

#### Answering "how much?"

`
1. `Financial > Query`. In `Rank` press `F9` — the `Ranks` list with a `Find` box that accepts `%` — pick the contract, then the `Relative`. For cash: Normal / Out Patient. 
2. In `Shortcut` type the test's shortcut or `F9` and pick it, as in `Patient Tests`. The row shows the ordinary cash price (`Normal Patient Rank`), the patient's and body's shares on this contract (`Cash` / `Insurance`) and the expected result date. 
3. Add the rest; totals sit under the grid. `Preparation` on a row shows that test's preparation (fasting…), and `All Preparation` gathers the whole list's. `
4. `Print` for a printed quote the patient can take. Nothing is written to the database.

**What you should see**`A cash total, a body total and a result date — the very numbers that would appear if you actually registered the visit on this contract. ⚠️ [Needs confirmation: the `Print` layout — not shown.]

`

### 10.8 · Refunding money — `Return Money`

**`

When is a patient owed a refund? When their balance goes **negative** in `Payment`: they paid cash and a test was cancelled (10.10), or their visit was moved to a contract that covers part (10.9). The negative figure is what you return. 01:09:50

[PROCEDURE]

#### Returning an amount and printing the refund receipt

**Path** `Financial > Return Money`

**Right** Sensitive — granted only to whoever holds the cash drawer (see the warning under 10.9)

`
1. The screen opens in query mode (`Enter a query; press F8 to execute`). Type the visit number into `Acc. No.`, then `F8`. 
2. The header (`Total Required` · `Paid` · `Remaining` · `Discount`) and the `Tests` grid with the visit's tests and prices appear. The amount to return shows in `Return Value` (68 EGP in the trainer's example). 
3. Cash or card? For card tick ☑ `Visa` and type the `Transaction No.`. `**
4. `F10`. A **refund receipt prints automatically**. Have the patient sign that they received the amount and keep the copy: it is your proof to management that the money went to the client and nobody else.

**What you should see**`The visit's balance in `Payment` is no longer negative, and the refund appears in the `استردادات` column of the daily report (10.11), reducing `الرصيد الفعلي`. 01:10:31

[FIGURE]  [IMAGE img/img-045.webp — شاشة Return Money]

_Caption:_ `**`**`Return Money`.** The visit header, the `Return Value` · ☐ `Visa` · `Transaction No.` section with `Print` · `Print All`, and the `Tests` grid (`Test Name` · `Price` · `Total`). 01:08:49

`

### 10.9 · Moving a visit to another contract or shifting a test's value — `Money Transfer

The everyday case: a company-contract patient arrives before the company's approval does. Reception registers them as `Normal / Out Patient` and takes cash; when the approval comes, the visit is moved to their contract, the contract takes its share and the patient gets back what they overpaid. The screen has two tabs. 01:11:45

[FIGURE]  [IMAGE img/img-046.webp — Money Transfer — Rank Transfer]

_Caption:_ `Money Transfer` ▸ `Rank Transfer`.** The visit header (`Acc. No.` 44087 · `Total Required` 132 · `Paid` 132 · `Remaining` 0), then two rows: the current contract `Rank` Normal / `Relative` Out Patient, and the new one `Rank` Lab to Lab / `Relative` Cash. 01:12:34

[PROCEDURE]

#### Moving a visit from cash to a contract once approval arrives

**Path** `Financial > Money Transfer` ▸ tab `Rank Transfer`

**Prerequisites** The visit number, and the body's approval in hand

`
1. `F7`, type the visit number into `Acc. No.`, `F8`. The header shows the current contract and what was paid. 
2. Open the `Rank Transfer` tab. The first row shows the current contract; in the second pick the new `Rank` with `F9`, then the `Relative`. `
3. `F10`. The visit's tests are re-priced at the new contract's prices (patient share + body share). `**
4. Open `Payment` on the visit: whatever the patient paid in cash above their new share shows **negative**. Return it with procedure 10.8.

**What you should see**`The visit sits on the new contract in every report, the body's share waits in the claim (10.13), and the patient's balance was zeroed by the refund. The transfer is logged under your name and shows in the `Pateint Transfer` report. 01:12:39

[FIGURE]  [IMAGE img/img-047.webp — Money Transfer — Normal Transfer]

_Caption:_ `**`**`Normal Transfer`**: a `Tests` grid with two paired columns — `Price List` (`Patient` · `Insurance`) as the list says, and `Actual Payment` (`Patient` · `Insurance`) as it will actually be charged. Here you move one test's value from the patient's share to the body's, or change it (20 becomes 15; 10 cash and 10 credit). 01:13:15

[NOTE:WARN] Sensitive screens: narrow rights and one account per person Return Money`, `Money Transfer` and `Delete Patient` let money out or drop receivables. The trainer is blunt: a clerk with rights wider than their job may "take the money for themselves" instead of refunding the client. Two protections: grant the right to as few people as possible, and make everyone work under their own login — every action is recorded under whoever performed it and shows to management in the reports, so whoever lends a password answers for what is done with it. 01:14:08

`

### 10.10 · Cancelling a test on a visit financially — `Delete Patient`

**

The name suggests deleting a patient; the screen actually cancels **a visit or some of its tests** for a recorded reason, leaving a trail in a `Log` and in the `Canceled Patients Or Tests` report. Nothing is physically erased. 01:09:31

[FIGURE]  [IMAGE img/img-048.webp — شاشة Delete Patient]

_Caption:_ ``Delete Patient`.** `Patient Data`: `Branch` · `Acc. No.` · `Patient No.` · `Reg. Date` · `Patient Name` · `Rank` · `Relative` · `Total` 275 · `Paid` 200 · `Remainig` 75 · `Discount` · ◯ `Canceled` ◉ `Active`. Grid: `Test Name` · `Status` · `Patient` · `Insurance` · `Result Date` · `Cancellation Reason` · ☐ `Canceled` · `Log`. 01:09:31

[PROCEDURE]

#### Cancelling one test from a paid visit

**Path** `Financial > Delete Patient
1. `F7`, the visit number into `Acc. No.`, `F8`. The visit's data and its tests appear with status and prices. `
2. On the row of the test to cancel tick ☑ `Canceled` and type or pick a `Cancellation Reason`. To cancel the whole visit choose ◉ `Canceled` in the header. 
3. `F10`. The row's `Log` button shows who cancelled, when and why. 
4. Open `Payment`: if the patient had paid for the cancelled test, their balance is now negative — refund it with `Return Money` (10.8).

**What you should see**The test left the visit's receivables and the worklists, and stays in the reports as "cancelled for reason". ⚠️ [Needs confirmation: the literal steps and the right — the audio for this screen was lost in transcription; the above comes from the screenshots and the screen's logic.]



### 10.11 · Closing the reception day — `Income` ▸ `Total Daily Income**`

The `Income` screen is not one report but a **report panel**: set the branch, user and period, then pick the report by its button. Its most important button for whoever holds the drawer is `Total Daily Income`, because it says how much cash should be in the drawer right now. 00:47:28

[FIGURE]  [IMAGE img/img-049.webp — شاشة Income]

_Caption:_ ``Income`.** `Branch` ▾ (All or one) · `User Name` · `Date From` · `Date To`. Buttons: `Patients' Income` · `Total Daily Income` · a `Payment Methods` group (`Branch` · `Group` · `Unit`) · a `Contract Type` group (`Branch` · `Group` · `Unit`) · `Monthly Statistics` · `Patient Totals`. 00:47:28

[PROCEDURE]

#### Handing over a shift: matching the drawer to the system

**Path** `Financial > Income
1. Pick your `Branch` and put your name in `User Name` (`F9`) to restrict the report to what you collected. The default period is today 00:00 to tomorrow 00:00; narrow it to your shift if you work shifts. `**`
2. Press `Total Daily Income`. The "**يومية خزينة استقبال مجمعة**" report opens: one row per payment (`الرقم` · `الاسم` · `الطبيب` · `تاريخ الدفعة` · `الدفعة` · `استردادات` · `الخصم` · `الباقي` · `المدفوع` cash/visa · `الاجمالي` · `الجهة`). `**
3. Read the summary box: `إجمالي الدفعات` − `إجمالي الاستردادات` = **`الرصيد الفعلي`**. That is the cash that must be in your drawer. Count the drawer and match. `
4. A difference? Go back to the rows: `الباقي` says who did not finish paying, `استردادات` what you refunded, `الخصم` what was discounted. Visa sits in its own column, not in the drawer. `
5. Print the report and sign it with whoever takes over. For management: the same screen with `Branch` = All and no user gives every branch, and `Total Daily Income` also yields the compact `Total Daily Income` report: `Day` · `Date` · `Paid` (`Cash` · `Visa`) · `Total` day by day ⚠️ [Needs confirmation: which button yields which of the two layouts].

**What you should see**`In the training example: one payment of 200 on a 275 visit ⇒ `إجمالي الدفعات` 200 · `إجمالي الاستردادات` 0 · `الرصيد الفعلي` 200 · `اجمالى البواقي` 75 · `الاجمالى` 275. The drawer holds exactly 200 EGP. 00:49:23

[FIGURE]  [IMAGE img/img-050.webp — تقرير يومية الخزينة]

_Caption:_ "يومية خزينة استقبال مجمعة"** (`casher_a`): header `من` · `الى` · `الفرع` · `اسم المستخدم`; rows per payment; the six-figure summary including `الرصيد الفعلي`. 00:49:23

### 10.12 · Income, statistics and doctor reports



Every report opens with a similar small dialog: `Branch` (red, mandatory) · `Date From` · `Date To` · optional orange filters (`Rank`, `Relative`, `User Name`, `Doctor Name`) · a `Print` button that opens the previewer. The table says what each one answers and where to find it. 01:18:25

| The question | Report and where | What it shows |
| --- | --- | --- |
| What did each patient pay in the period? | `Income` ▸ `Patients' Income` (and the item `Patient Income`) | "Patients' Income": `اسم المريض` · `الاجمالي` · `قيمة الخصم` · `بعد الخصم` · `المدفوع` · `باقي الحساب` + a totals row. 00:45:17 |
| How much came in cash vs card? How much from cash vs credit contracts? | `Income` ▸ `Payment Methods` / `Contract Type` × `Branch` · `Group` · `Unit` | `Payment Methods` splits income by how it was paid; `Contract Type` by the Rank's classification (cash/credit/insurance). Each at branch, group or unit level. 00:51:30 |
| What did each contract bring month by month? | `Income` ▸ `Monthly Statistics` | `"Monthly statistics": `Rank` · a column per month · `Total` · `Average`. Use a whole month or only the Rank registered that day appears. 00:53:22 |
| A statement for a visit or a patient | `Income` ▸ `Patient Totals` · item `Patient Total` | "Patient Financial Status": `Acc. No.` · `Patient No.` · `Patient Name` · `Reg. Date` · `Total` · `Discount` · `Paid` · `Return` · `Net Paid` · `Remaining`. The `Patient Total` item asks for a `Patient No.` and gathers their visits. 01:00:17 |
| Which doctor referred how many patients and for how much? | `Doctor Income` | `Dialog: `Branch` · `Doctor Name` · `Rank` · `Relative` · the two dates · `Sort By` (`Name` / `Patient Count`) · ☐ `Print Details` · ☐ `Special Customer` · `Print`. The "Doctors' Income" report: each doctor, then their patients (`تاريخ الطلب` · `كود المريض` · `اسم المريض` · `الاجمالي` · `قيمة الخصم` · `بعد الخصم` · `المدفوع` · `باقي الحساب`). `Special Customer` is for a doctor with a `Share` (a percentage of their patients, say 20%) set in `Setup ▸ General Data ▸ Doctors`. 00:39:31 |
| What did a given contract bring? | `Reports ▸ Clients Income` | `Branch` · the dates · `Rank` · `Relative` · `Print` ⇒ detailed income per contract. 01:20:24 |
| How often was a given test registered, and for whom? | `Reports ▸ Test Income Analysis` | "Test Income Analysis": per `Rank Name` — `Tests Count` · `Cash` · `Credit` · `Total`. Leave `Test` empty for all tests. 01:23:36 |
| Who still owes? And what were the balances on a given day? | `Reports ▸ Remaining` | `**_`Branch` · the dates · `User Name` · **`Remaining Date`**. Without a date: balances now. With one (15-03-2022): balances _as they stood that day_, even if settled later — for a historical reconciliation. Format `dd-mm-yyyy hh24:mi` or `FRM-50025`. 01:25:19 |
| Who gave a discount, and why? | `Reports ▸ Discounts` | **The period's discounts: patient, amount, user, and the **discount reason** chosen at registration. 01:27:45 |
| What was cancelled and who was transferred? | `Reports ▸ Canceled Patients Or Tests` · `Pateint Transfer` | The trail of `Delete Patient` and `Rank Transfer` ⚠️ [Needs confirmation: not shown] |
| Each day's total | `Reports ▸ Total Income Per Day` | `Branch` · the dates · `Print`. 01:29:21 |
| Charts | `Reports ▸ Income Analysis` · `Ranks Monthly Statistics` · `Rank Periodic Curve` | Do not work in the desktop edition on 64-bit Windows** — ignore them. 01:23:04 |

[FIGURE]  [IMAGE img/img-051.webp — Financial Reports submenu]

_Caption:_ ``Reports ▸`**: `Print Client Fees` · `Print Client Fees With Sample` · `Clients Income` · `Test Income Analysis` · `Income Analysis` · `Remaining` · `Pateint Transfer` (sic) · `Ranks Monthly Statistics` · `Rank Periodic Curve` · `Canceled Patients Or Tests` · `Discounts` · `Total Income Per Day`. 01:18:25

[FIGURE]  [IMAGE img/img-052.webp — حوار Doctor Income]

_Caption:_ ``Doctor Income`** — `Sort By` takes `Name` or `Patient Count`; `Comparison` is disabled in this build. 00:36:38

[FIGURE]  [IMAGE img/img-053.webp — تقرير Monthly statistics]

_Caption:_ "Monthly statistics"** (`rank_mon_tot`) for 03/2022: a row per `Rank` with `Total` and `Average`. 00:54:24

[FIGURE]  [IMAGE img/img-054.webp — تقرير Test Income Analysis]

_Caption:_ "Test Income Analysis"** (`global_tests`) for `ALT (SGPT)`: `SN` · `Rank Name` · `Tests Count` · `Cash` · `Credit` · `Total`. 01:23:36

[FIGURE]  [IMAGE img/img-055.webp — حوار Remaining]

_Caption:_ `**`**`Remaining`**: period 01-03 → 29-03 and `Remaining Date` 15-03-2022 ⇒ March visits' balances as they stood on the 15th. 01:27:42

`

### 10.13 · Billing a credit contract — `Invoice`

A "claim" is the statement you send a company or syndicate every month (or week, per the contract) listing its patients, their tests and its share of every visit. It means nothing for a cash contract: cash was collected and that was that. 00:55:54

[FIGURE]  [IMAGE img/img-056.webp — شاشة Invoice]

_Caption:_ ``Invoice`.** Left: `Branch` ▾ · `Lab` ▾ · `Rank` · `Relative` · `Date From` · `Date To` · `Order By` ▾ (Reg Date) · ☐ `Excel`, and six buttons: `Invoice` · `Invoice with Relative` · `Invoice With Results` · `Invoice With Totals` · `Credit` · `Invoice With No. of tests Counts`. Right: `Acc. No.` · ☐ `Old Data` · a `Selected Patients` grid (`Acc. No.` · `Patient Name` · `Reg. Date`). 00:56:31

[PROCEDURE]

#### Issuing a monthly claim to a body

**Path** `Financial > Invoice
1. Pick the `Branch`. To bill all branches together leave it empty ⚠️ [Needs confirmation: an "All" value]. `Lab` is for labs running `Lab To Lab`: a claim to a sending lab instead of a contract body. `
2. Pick the body's `Rank` (`F9`). Leave `Relative` empty for one claim covering all tiers, or set it for a single tier. `
3. Set the period (`Date From` / `Date To`) — by registration date — and the row order in `Order By`. For specific visits instead of a period: type their numbers into `Acc. No.` and they enter `Selected Patients`. 
4. Pick the layout the body wants: the standard `Invoice` · `Invoice with Relative` split per tier · `Invoice With Results` with the test results (some bodies ask for it) · `Invoice With Totals` totals only · `Invoice With No. of tests Counts` with test counts. ☑ `Excel` exports a file where enabled (a customisation). `
5. The previewer opens. Check the last page's total, print, and sign the `إعداد` · `مراجعة` · `إعتماد` boxes before sending.

**What you should see**`A report with columns `م` · `رقم البطاقة العلاجية` · `اسم العضو` · `اسم المريض` · `رقم الزيارة` · `تاريخ التسجيل` · `ح كلي` · `ح مشروع` · `ح مريض`, with `إجمالي الصفحة` on every page and `إجمالي التقرير` on the last (213648.6 in the training example over 322 visits). The claim's layout is customised per body to what it asks for. 00:59:31

[FIGURE]  [IMAGE img/img-057.webp — تقرير Invoice]

_Caption:_ A claim** (`Invoice2_tot`): page 9 of 9 — `إجمالي الصفحة` 15723.6 · `إجمالي التقرير` 213648.6 · `إعداد` / `مراجعة` / `إعتماد` · printer and print time. 00:59:31

### 10.14 · A price list to hand out, costs, and the legacy screens

| Item | What it does and for whom |
| --- | --- |
| `Reports ▸ Print Client Fees` | **`A contract tier's price list **to hand to doctors or the client**, with the lab's logo and columns `Group Name` · `Test Code` · `Test Name` · `Arabic Name` · `Paid by Patient` · `Insurance`. `With Sample` adds the sample type ⚠️. 01:17:25 |
| `Tests Costs` | `**The `Test Cost` window: `Test Name` · `Cost` — **the test's actual cost to the lab**. Used by reports comparing cost to income over a period: 100 COVID tests at cost 10 = 1000 against what they really brought in after discounts ⇒ profit or loss. 01:15:20 |
| `Test Fees` | `A legacy screen pricing one test across all contracts (`Group Name` ▾ · `Test Name` · a grid of `Rank Name` · `Relative Name` · `Test Name` · `Patient` · `Insurance`). Price lists replaced it; not used. 01:14:57 |
| `panel_price` | A single client's customisation (panel test details and prices). Not part of the standard build. |
| `Additional Services Report` · `Representative Income` | `The "Patients' Services" report (`Patient No.` · `Patient Name` · `Patient Total` · `Insurance Total` · `Count`) for extra services such as `Internet Service` and `SMS Service`; and lab representatives' income. ⚠️ [Needs confirmation: not explained] |

[FIGURE]  [IMAGE img/img-058.webp — تقرير Price List للتوزيع]

_Caption:_ **`**"Price List"** (`Fees`) under the Al-Barq logo: `Rank Name` Lab to Lab · `Relative Name` Cash, then the tests group by group with what the patient pays. 01:17:25

### Worked scenarios — with the training numbers

#### 1 · Next year's prices, +10%, for twenty contracts

The accounts officer in January. The base list "Main price list2022" (CBC = 80) feeds twenty contracts through different formulas.


1. `Main Price List`: `Add New Record`, `Code` 22, "Main price list2023", `F10`. 
2. `Copy price list`: `Old` = 2022, `New` = 2023, multiply × 1.1, `Apply` → "created successfully". CBC is now 88. 
3. `Client Price List` for each of the twenty tiers: switch the list in the boxes from 2022 to 2023 keeping the factors, `F10`, `Apply`. `Print` gives the full table for the manager's review. `
4. Tests with a negotiated price in some contract were flagged `Exclude`, so they stayed as they were.

**Outcome**Registration shows 2023 prices for every contract from the next day, without a single price typed by hand.

#### 2 · A new contract: 90% cash off the institution list, with conditions on certain tests

"Coca-Cola", tier "employees and families": the patient pays 90% of the "المؤسسة العلاجية 2017" prices and the company covers nothing; tests missing from that list come from "المكملة العلاجية" × 0.7; allergy tests need approval; Cow milk has a negotiated price.


1. `Price Lists`: Rank "شركة كوكاكولا" code 37 → Relative "عاملين وأسرهم" → `F10`. `
2. `Client Price List`: `First patient` "المؤسسة العلاجية 2017" X.9 · `First insurance` same X 0 · `Second patient` "المكملة العلاجية" X.7 · `Second insurance` X 0 → `F10` → `Apply`. `
3. Back in `Price Lists`: ☑ `Alert` on the allergy tests and write the condition under `Conditions`; type Cow milk's price by hand and tick ☑ `Exclude`. `F10`.

**Outcome**`In the `Client Price List` report the Coca-Cola row reads as in the screenshot: ".9 /.7 / 0 / 0". And when reception registers an Allergy Screen for a Coca-Cola employee, the condition message pops up.

#### 3 · Paid cash, then the approval arrived — transfer and refund

Visit 44087, TEST, registered Normal / Out Patient with 200 of 275 collected. A contract approval arrives covering a large share.

`
1. `Money Transfer`: `F7` 44087 `F8` → `Rank Transfer` → Rank Lab to Lab / Relative Cash → `F10`. Re-pricing makes the patient's share 132. 
2. `Payment` on 44087: `Remaining` shows −68. `
3. `Return Money`: `F7` 44087 `F8` → `Return Value` 68 cash → `F10` → a refund receipt the patient signs.

**Outcome**`The visit's balance is zero; the cash-desk daily shows a 200 payment and a 68 refund ⇒ `الرصيد الفعلي` 132; the company will find the visit on its monthly claim. 01:10:15

#### 4 · Month end: the company's claim and closing the day

The accounts officer on 29 March, and the reception clerk at the end of a shift.

`
1. Reception: `Income` → their branch and name → `Total Daily Income` → `الرصيد الفعلي` 200 = what is in the drawer. Print and sign. 
2. Accounts: `Invoice` → branch, the company's Rank, 01-03 → 29-03, `Invoice With Totals` → 9 pages, report total 213648.6 → sign preparation/review/approval → send. `
3. Accounts as well: `Reports ▸ Remaining` with `Remaining Date` = 31-03 for March's balances as they will close, and `Discounts` to review who discounted and why.

**Outcome**A reconciled drawer, a signed claim, and a balances-and-discounts list ready for the management meeting.

### Questions staff ask

**I changed a test's price in the base list and reception still sees the old one**

`

You did not press `Apply`. Registration reads the contract list, not the base list, and it refreshes only on `Apply`. Also check ☑ `Active`.

**I created a contract and its price list is all zeros**

`

Normal. It is not tied to a base list yet: `Client Price List` → the formula → `F10` → `Apply`.

**One test in the contract has a special price — every Apply resets it**



Tick ☑ `Exclude` on it in `Price Lists`. The formula leaves excluded rows alone.

**What is the difference between Patient and Insurance in a contract list?**

Patient` is what the patient pays in cash now; `Insurance` is what the body pays later and is billed for in `Invoice`. Together they make the test's price on that contract.

**The patient wants the price before registering**

Financial > Query`: the contract, then the tests via `Shortcut`; cash, credit and result date, and `Print` for a quote. Nothing is registered.

**The patient's balance in Payment is negative — what now?**



They paid more than due (a cancelled test or a contract transfer). `Return Money`: `F7` the visit `F8`, the amount, cash or visa, `F10`, and get their signature on the refund receipt.

**I registered the patient as cash and then the company's approval came**

Money Transfer` ▸ `Rank Transfer` to the company's contract, then `Return Money` for the excess. Don't cancel and re-register the visit.

**How much must be in the drawer at the end of the shift?**

Income` → your name and period → `Total Daily Income` → `الرصيد الفعلي`. Visa is not in the drawer.

**Monthly Statistics shows only one contract**

The period is a single day. Make it the whole month (1st to 30th) and all contracts appear.

**The chart report is empty**

Income Analysis`, `Rank Periodic Curve` and `Ranks Monthly Statistics` do not work on 64-bit Windows in the desktop edition. Use the tables.

**What does Remaining Date do?**

`

It shows the period's balances as they stood on that date, even if settled afterwards. Without it you get the current position. Format `dd-mm-yyyy hh24:mi`.

**Does Delete Patient erase the patient from the database?**



No. It cancels a visit or tests for a recorded reason, and everything stays in the `Log` and the `Canceled Patients Or Tests` report.

**`**What needs confirmation in this chapter** (also collected in chapter 21): the rights for the financial screens · `Type` values for a Relative other than `Normal` · how `Restore from main price list` treats `Exclude` · whether the second and third lists are fallbacks for tests missing from the first · the meaning of `Money Format` · the literal `Delete Patient` steps and right · which `Income` button yields the combined cash-desk daily and which the compact "Total Daily Income" · an "All" value in `Invoice` · the `Query` printout · the `Canceled Patients Or Tests`, `Pateint Transfer`, `Additional Services Report` and `Representative Income` reports · the `Share` field in doctor setup · 9.3.0 vs 9.2.0 differences on these screens · the whole `Outsourcing` module.

---

<!-- section c11 -->

## 11Test setup — from one test to a whole packageComplete

`**

Everything reception sees on the order screen, everything printed on a patient's report, and every result that turns red or gets refused — all of it comes from this chapter. `Setup ▸ Tests Setup` is where the **catalogue** is built: the single test, its group, the profile, the mega, the culture, the text report, the package, and the rules that tie them together. The chapter speaks in the voice of the **application engineer** who builds the catalogue once at installation and edits it on request afterwards. Every hh:mm:ss points into the **session 9** recording.

[NOTE:WARN] This session was recorded on the 9.3.0 build 

The splash reads `LDM 9.3.0` — "Al-Barq Lab", like chapter 10 and unlike the `9.2.0` training build of chapters 05–09. Differences are small but real: extra fields on `Tests`, the `Undo Verify` · `Undo Review` · `Print Logo` buttons on the results screen, and the `Lab` and `Governorate` fields. 00:56:53

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 11.1 | `The seven types and a map of `Tests Setup` | Everyone |
| 11.2 | `Creating a new test — the `Tests` screen | Application engineer |
| 11.3 | `Numeric reference ranges — `Test Ranges` | Engineer + chemist |
| 11.4 | `Text ranges — `Test Conditions` and the `Normal`/`Exact` rule | Application engineer |
| 11.5 | A calculated test — `Formula` and `Formula Setup` | Application engineer |
| 11.6 | Comments and methods — `Test Comments` · `Test methods` | Chemist |
| 11.7 | `Durations and alerts — `Panic and reportable range` | Engineer + quality |
| 11.8 | `A profile — the `Profiles` screen | Application engineer |
| 11.9 | `A mega profile — the `Mega Profiles` screen | Application engineer |
| 11.10 | `The print template — `Report Type` | Application engineer |
| 11.11 | `Cultures and the `Culture Link` | Engineer + microbiologist |
| 11.12 | Text reports and templates — `Text Tests` · `Text Test Templates` | Engineer + pathologist |
| 11.13 | `A single-price package — `Panels` | Engineer + accounts |
| 11.14 | `Auto-add rules — `Test Rules` | Application engineer |
| 11.15 | `Entry shortcuts — `Tests Shortcuts` | Application engineer |
| 11.16 | `Unified codes — `Unique Services Codes` | Engineer + integration |

`

### 11.1 · The seven types and a map of `Tests Setup`

**

Before opening any screen: every catalogue item has a numeric **type** in the database. The type decides the shape of the setup screen, the shape of the result-entry screen, the print template, how it is priced, and how analyser integration treats it. Get the type wrong and you build the item twice. 00:00:00

| # | Type | What it is | Where it is set up |
| --- | --- | --- | --- |
| 1 | `Test` | A standalone test with one result (ALT, Creatinine) | `Tests` — 11.2 |
| 2 | `Profile` | **A set of tests **from one group** ordered and printed together (Lipid profile) | `Profiles` — 11.8 |
| 3 | `Mega profile` | **A set of **profiles** only (CBC = Total + Absolute + Relative) | `Mega Profiles` — 11.9 |
| 4 | `Culture` | A culture: organisms, antibiotics and sensitivity instead of a number | `Setup ▸ Culture` — 11.11 |
| 5 | `Text` | A free-text report with no reference range (Histology, Cytology) | `Text Tests` — 11.12 |
| 6 | `Panel` | **A package mixing tests, profiles and megas **across groups** at one price | `Panels` — 11.13 |
| 7 | `Additional service` | A service, not a test: internet, SMS, delivery, home visit, urgency — chargeable, with no result | `The `Additional Services` group |

[FIGURE]  [IMAGE img/img-059.webp — قائمة Tests Setup]

_Caption:_ ``**The `Setup ▸ Tests Setup` menu**, verbatim: `Tests` · `Text Tests` · `Text Test Templates` · `Profiles` · `Mega Profiles` · `Test Rules` · `Panels` · `Tests Shortcuts` · `Fonts ▸` · `Unique Services Codes`. Note that **cultures are not here** — they have their own `Setup ▸ Culture` branch. 01:28:31

[NOTE] The right build order 

Always bottom-up: units and samples in `General Data` → groups in `Groups` → single tests → profiles → megas → packages, rules and shortcuts. Any layer built before the one beneath it will send you back to it twice.

`

### 11.2 · Creating a new test — the `Tests` screen

[FIGURE]  [IMAGE img/img-060.webp — شاشة Tests]

_Caption:_ `**`**The `Tests` screen.** Left: `Group Name` · `Report Name` · `Unit` · `Sex` · ☐ `Visible in job order` · the `Test Type` list (`Age Dependant` · `Non_numeric Test` · `Formula`) with its `Test Ranges` · `Test Conditions` · `Enter Formula` buttons. Right: `Test Name` · `Arabic Name` · `Sample` · `Sample Condition` · ☑ `Visible` · ☐ `Biohazard` · ☐ `Last Result` · `Print Ref From` · `Header` · `Test Methodology` · ☐ `Print Report With QR Code`. Bottom: `Code` · `Test Comments` · `Test methods` · `Panic and reportable range` · `Fonts`. The window title becomes `<Group> - <Test Name>` once saved. 00:55:24

[PROCEDURE]

#### Defining a new numeric test end to end

**Path** `Setup > Tests Setup > Tests`

**Right** Application engineer or system admin. ⚠️ [Needs confirmation: the right's name]

**Prerequisites** `The group exists in `Setup ▸ Groups` · the unit in `General Data ▸ Results Units` · the sample and its condition in `General Data ▸ Samples
1. `F6` for a new record, then pick `Group Name` with `F9`. The group decides the test's print template (11.10) and its place in the order grid. `**
2. `Test Name`: a **unique internal name across the whole system**. If you have an ALT on an analyser and an ALT sent out to another lab, name them differently here. `**
3. `Report Name`: what the **patient** reads on the report. Two tests may share a `Report Name` ("ALT") with different internal names. `Arabic Name` is for the Arabic report or label. 
4. `Unit` via `F9` from the `Results Units` list. Do not type it — if it is missing, add it first in `General Data ▸ Results Units`. `**
5. `Sample` then `Sample Condition` — **both from the list with `F9`**. The condition belongs to the sample, so pick the sample first. 
6. `Sex`: `Male` / `Female` / `Both`. The wrong choice here blocks reception from ordering the test for the other sex with a "for male only" message. `
7. Tick ☑ `Visible` so reception can see it. Leave ☐ `Visible in job order` clear for lab-to-lab or internal tests you do not want on the job order. Tick ☐ `Biohazard` for hazardous samples (HIV, HBV). 
8. Pick a `Test Type`: `Age Dependant` for a numeric result (11.3), `Non_numeric Test` for a text result (11.4), `Formula` for a calculated one (11.5). The matching button is the one that lights up. 
9. `Header` is optional: a sub-heading printed above the test on the report, used to group scattered tests under one banner. `Test Methodology` is the method's name (printed depending on the template). 
10. `F10` to save. Then fill in `Panic and reportable range` (11.7) — a test with no duration gets no delivery time.

**What you should see**`The window title now reads `<Group> - <Test Name>` and the status bar `Data has been saved successfully.`. Try it straight away from `Registration ▸ Patient Tests`: the test appears under its group, has a price if priced (chapter 10), and prints a barcode.

[NOTE:BAD] The test saved but never shows at registration and prints no barcode `**`**

First cause: `Sample Condition` was **typed by hand** instead of picked with `F9`. The row saves but the link to the sample is never formed, so no barcode is issued and the test never appears in search. **Only a database fix works** — the screen cannot repair it. Second cause: ☐ `Visible` is not ticked. Third: `Sex` does not match the patient you are testing with.

[NOTE:TIP] Ten tests in one sitting — `F6` then `F4**`

Building the catalogue means entering dozens of tests that mostly share everything: the same `Group Name`, `Sample`, `Sample Condition` and durations. Do not retype them. Enter the first one fully and save it, then for each one after: `F6` (new record) → `F4` (copies the previous record in full) → change **only what differs** (the unique `Test Name`, the `Report Name`, the reference range) → `F10`. The detail and the warning are in chapter 03 §9.

[NOTE:TIP] `The `Fonts` button — a font per element `**

The last button at the foot of `Tests` styles **each part of the test's report line separately**: `Test Name`, `Result` and `Unit`, each with its own font, size, justification (right/left), `B`, `I` and `U`. So a test's name can print in a different face from its result and its unit. Use it sparingly: fonts that differ from test to test make the report look inconsistent. 00:55:52

[NOTE] Why this screen takes so long 

The trainer's line is that `Tests` is the slowest setup screen "because everything is in it". The rest of `Tests Setup` goes far faster because it reuses the same ideas: `Group`, `Report Name`, `Sample`, `Sex`, the durations, `Visible`, `Code`. Learn this screen once and you will read the others quickly. 00:55:21

`

### 11.3 · Numeric reference ranges — `Test Ranges`

The `Age Dependant` type means the normal range moves with age — true of most tests. The `Test Ranges` button opens a table with one row per age band, each with a `From` and a `To`. A result outside the band is shaded on the results screen (pink for high, orange for low) and printed marked on the report. 00:19:50

[NOTE] `Which range gets printed — `Print Ref From`**

The `Print Ref From` field on the `Tests` screen decides **where the printed text comes from** in the report's reference column: the numeric `Test Ranges`, the `Age Comments`, or `All`. Choose `Age Comments` when the range is a sentence rather than two numbers. 00:35:55

`

### 11.4 · Text ranges — `Test Conditions` and the `Normal`/`Exact` rule

`

The `Non_numeric Test` type is for a test whose result is a word, not a number (Negative / Positive / Not detected). The `Test Conditions` button opens the `Test Result Conditions` dialog: the list of allowed values, each with two boxes — ☐ `Normal` and ☐ `Exact`. 00:21:30

| Box | What it means |
| --- | --- |
| ☑ `Normal` | **This value is **normal**: it is not shaded on the results screen and raises no flag. An unticked value counts as abnormal and is shaded. |
| ☑ `Exact` | **This value is the **default** that drops into the result box when the test is ordered. |

[NOTE:BAD] `🚫 Never make `Negative` the default value **`**`

Tick ☑ `Exact` on `Negative` and every patient **whose test has not been run yet** shows "Negative" on the results screen — and anyone glancing at it reads that as an issued result. The right order: the first record is a **dot `.`** or blank with ☑ `Normal` ☑ `Exact` (so the default is empty), then `Negative` with ☑ `Normal` only, then `Positive` with nothing ticked. 00:21:30

[NOTE:TIP] The dot fills the box `**

In many-parameter tests (urinalysis, say) the chemist cannot fill every box, and the system may refuse to save with one missing. A dot `.` is a valid value that saves **and does not print** — the clean way to close boxes you do not need.



### 11.5 · A calculated test — `Formula` and `Formula Setup**

The `Formula` type is for a test that is **not measured** but computed from other results (A/G Ratio, calculated LDL, the ratios in a blood count). The `Enter Formula` button opens the `Formula Setup` dialog where you pick the component tests and write the equation. The result fills itself once its components are in, and nobody types it. 00:24:02

[NOTE] Formula or rule? `**

These two get confused. A **formula computes a result** from results already there; it adds no test and no charge. A **rule** (`Test Rules`, 11.14) **adds tests** alongside tests, and each test it adds is charged at its own price and enters no equation.



### 11.6 · Comments and methods — `Test Comments` · `Test methods

Two buttons at the foot of the `Tests` screen serve two different audiences:

``
- **`Test Comments`** — notes printed **for the patient and doctor** under the result: how to read the range, a warning about a drug that skews it, or a long text range that will not fit the `Test Ranges` table. 00:38:58 `**
- **`Test methods`** — the steps for running the test (a short SOP) shown **to the chemist** from the results screen and never printed for the patient. The button is **absent in some builds**. 00:36:31
`

### 11.7 · Durations and alerts — `Panic and reportable range`

**

This dialog is the **test's brain**: it is where the system learns when the test is due, when to alarm, and when to refuse a repeat order. Open it from the `Panic and reportable range` button at the foot of `Tests`. 00:44:18

[FIGURE]  [IMAGE img/img-061.webp — حوار Panic and reportable range]

_Caption:_ `**`**The `Panic and reportable range` dialog.** `No. of labels` · `Prefix` · `Result Format` (0.000) · `Low Reportable` / `High Reportable` · `Low Panic` / `high Panic` · `Duration in Days` / `Duration in Hours` · `Frequency Check Days` / `Frequency Check Hours` · `Min Duraion Days` / `Min Duraion Hours` (the build's own misspelling) · `Delta Check` / `Delta Check Days` · a seven-day `Test Days` box · a `Back` button. 00:51:01

| Field | What it does |
| --- | --- |
| `Result Format` | How many decimals are printed (0.000 = three). |
| `Low`/`High Reportable` | The limits the analyser can measure. Outside them the result prints as "less than" or "more than" instead of a meaningless number. |
| `Low`/`high Panic` | Life-threatening limits. Crossing one raises an alarm that requires calling the doctor at once, and colours the results screen (dark red / yellow). |
| `Duration in Days`/`Hours` | **The turnaround time — the basis for the delivery date and **patient TAT** (registration to verification). |
| `Min Duraion` | **The shortest possible time — the basis for **lab TAT** (sample receipt to verification), the figure the lab is judged on internally. |
| `Frequency Check` | The shortest gap allowed between two orders of the same test for the same patient. Breaking it gives an **Alert** (warn and continue) or a **Prevent** (block until the time passes). |
| `Delta Check` | `A percentage plus `Delta Check Days`: compares the result with the patient's previous one inside that window; a swing bigger than the percentage warns at verification. |
| `Test Days` | **`The weekdays the test runs. **An unticked day does not count toward the duration**: register on a Friday with Friday unticked and the `Duration` clock only starts Saturday morning; register on Thursday and it counts Thursday, skips Friday, and resumes Saturday. It moves the delivery date, the `result date` and the `lab date` together. |
| `No. of labels` · `Prefix` | **`How many barcode labels print for this test, and a prefix written **before the test's name** on the label and report (`S.` for serum, say). |

[FIGURE]  [IMAGE img/img-062.webp — Delta Check = 10.00%]

_Caption:_ ``Delta Check` = 10.00%.** The trainer's example: a patient's haemoglobin was 14 yesterday and reads 8 today with nothing on file to explain it. Verification raises a Delta Check warning, you call the patient and ask for a second sample before releasing anything. "Your reputation stays intact, and you never issue a wrong result." 00:53:02

[NOTE] Standards before guesswork `

The `Panic` limits, `Delta Check` percentages and `Frequency Check` intervals are not a matter of opinion: published standards exist per test. What happens in practice is that the lab types an approximate value and the quality officer later checks it against the reference. Ask quality for them before installation, not after.

[NOTE:WARN] `The `Frequency Check` limit across branches **

The trainer said the check does not catch orders coming from **different branches or locations** — a patient repeating the test at another branch slips through. ⚠️ [Needs confirmation on a current build]

`

### 11.8 · A profile — the `Profiles` screen

[FIGURE]  [IMAGE img/img-063.webp — شاشة Profiles]

_Caption:_ `The `Profiles` screen.** Header: `Profile Name` · `Arabic Name` · `Report Name` · `Report Type` · ☑ `Visible` · `Group` · `Test Sum` · `Profile Sum` + `Code` · ☑ `Visible In Rep` · `Sex` · `Font` · `Font Size` + alignment · `Font Style`. Body: `Group Tests` on the left and `Profile Tests` on the right (`Name` · `Type` · `Order` · `Sum` · `Test Sum` · `Visible`) with `>>` and `<<` between. Here `Profile Sum` = 50 with three of four tests ticked in `Sum`. 01:03:00

[PROCEDURE]

#### Building a profile and setting its order and sum

**Path** `Setup > Tests Setup > Profiles`

**Right** Application engineer. ⚠️ [Needs confirmation]

**PrerequisitesEvery component test defined and **all in one group**


1. `F6` for a new record, then type the `Profile Name`, `Arabic Name` and `Report Name` (what the patient sees). **
2. Pick the `Group`. The `Group Tests` pane fills at once with that group's tests — **and will never show any other**. **`
3. Move the tests: a **double-click** sends a test to `Profile Tests`; or tick several and press `>>`. `<<` removes. `
4. Type an `Order` number on each row (1, 2, 3…) — that is their order on the report; there is no drag and drop. **
5. If the profile has a binding sum (a differential count = 100, say): type it in `Profile Sum` and tick the `Sum` column on **only the tests that enter the sum**. Leave the rest clear. `
6. Tick ☑ `Visible` on every row that should appear on the patient's report. Untick the helper tests. 
7. In the header: ☑ `Visible` so reception can order it, ☑ `Visible In Rep` so its name prints as a heading. Set `Font`, `Font Size` and `Font Style` for that heading. `F10`.

**What you should see**`The profile shows at reception as one item ordered with a click, and at result entry its tests appear in the order you typed. If you set a `Profile Sum`, entering results that do not add up refuses the save with a message.

[NOTE:TIP] The dot rule — hidden tests **

In the figure above, tests named `.Lymphocytes` and `.Neutrophils` begin with a dot. That is a catalogue convention: **a test whose name starts with a dot is never printed**, even when it has a result. It is used for helper tests that feed a formula or a sum and whose number means nothing to the patient.

[NOTE:BAD] The test I want is not in the left-hand pane **

Because a profile **only accepts tests from its own group**. If you want to mix Chemistry and Hematology tests into one item you do not want a profile: you want a `Panel` (11.13) if the point is a single price, or a `Tests Shortcut` (11.15) if the point is only faster entry. 00:59:50

`

### 11.9 · A mega profile — the `Mega Profiles` screen



A mega profile is a layer above the profile: **a set of profiles** ordered together. The classic case is a full blood count — one profile for the totals, one for the percentages, one for the absolutes, all ordered with a single click and printed as one report. A mega **takes no single tests**, profiles only. 01:03:25

[FIGURE]  [IMAGE img/img-064.webp — شاشة Mega Profiles]

_Caption:_ `The `Mega Profiles` screen.** Header: `Mega Name` · `Arabic Name` · `Report Name` · `Report Type` · ☑ `Visible In Rep` · ☑ `Visible` · `Group` + `Code` · `Sex` · `Name Font` · `Font Size` + alignment · `Font Style`. Body: `Group Profiles` on the left, `Mega Profiles` on the right (`Profile Name` · `Order`), with `>>` and `<<` between. 01:04:10

[PROCEDURE]

#### Building a mega profile

**Path** `Setup > Tests Setup > Mega Profiles`

**Prerequisites** `The component profiles are ready (11.8) and all in the same `Group`
1. `F6` for a new record, type the `Mega Name`, `Arabic Name` and `Report Name`, then pick the `Group` and the `Group Profiles` pane fills. 
2. Move the profiles with a double-click or `>>`, and type an `Order` for each. 
3. Set the `Report Type` — the mega's print template (11.10). For a culture mega it is `New_Mega_culture`. 
4. Decide how the inner profiles behave: go back to each one and clear ☐ `Visible` if reception should not order it alone, and keep ☑ `Visible In Rep` if you want its name as a heading on the report. `
5. Set `Name Font`, `Font Size`, alignment (`Center` is common for mega headings) and `Font Style`. `F10`.

**What you should see**The mega alone on the order screen, and one printed report carrying the profile headings in order with their tests beneath each.

[NOTE] `Visible` and `Visible In Rep` are independent switches **

Do not assume that hiding a profile from ordering hides it from the report. `Visible` = can reception see and order it? `Visible In Rep` = does its name print as a heading? Combining them gives you one orderable mega, readable headings inside it, and profiles nobody can order on their own. A profile that **is also sold alone** (the lab offers a "full CBC" and a "short CBC") keeps ☑ on both. 01:04:32

[NOTE:TIP] 🔑 The mega is the report unit — and how to use it for a single test **

The rule that explains a lot: **every mega profile prints as its own separate report**. Hence a use that looks odd and is common in practice: **a mega profile holding one profile holding one test**. Why? Because you want that particular test to come out **on a report sheet of its own** rather than merged with others — a sensitive test, one a client wants reported alone, or one with its own letterhead. There is no other way to force a separate report. National Technology material 2026-09-10

`

### 11.10 · The print template — `Report Type**

`Report Type` changes no data — it only decides **which template** the item prints on. Templates are built at installation; the standard values seen in this build are: 01:08:38

| Template | Used by |
| --- | --- |
| `new_test` | **The single test **and the profile** — a profile is, in the end, a set of tests printed in one table. |
| `new_mega` | The ordinary mega profile. |
| `New_Mega_culture` | The culture mega — a template that knows how to print organisms, antibiotics and sensitivities. |
| `new_text` ⚠️ | The text report. ⚠️ [Needs confirmation of the literal name] |

[NOTE] Where is the single test's template? **

The `Tests` screen has no `Report Type`. A single test **inherits its group's template**: in `Setup ▸ Groups` each group carries its own (`Allergy new test`, `Cancer new test`, `Hematology new test`…). So to change how a whole group prints, edit the group's template once — do not open a hundred tests. 01:09:42

[NOTE:WARN] Fields not seen on screen 

The trainer mentioned `Case Sums Chapter` and `Small` as controls over what a mega shows on the report, but neither field appeared in any frame of this session. ⚠️ [Needs confirmation on a newer build] 01:08:34

`

### 11.11 · Cultures and the `Culture Link

A culture's result is neither a number nor a single word: organisms that grew, antibiotics for each, and a sensitivity for each antibiotic. That is why it gets a whole setup branch of its own, outside `Tests Setup`. 01:10:45

[FIGURE]  [IMAGE img/img-065.webp — قائمة Setup Culture]

_Caption:_ `The `Setup ▸ Culture` menu:** `Antibiotics` · `Culture Antibiotics` · `Culture Organisms` · `Culture Tests` · `Organisms` · `Organisms Antibiotics` · `Sensitivity`. Behind it, the `Ascitic Fluid:Exam.,Chem.& C/S.` profile whose last test is `ascitic Swab Link` with `Type` = `Culture` at `Order` 11 — that is the culture link. 01:13:45

[FIGURE]  [IMAGE img/img-066.webp — شاشة Culture Tests]

_Caption:_ `The `Culture Tests` screen.** `Group Name` · `Culture Name` · `Report Name` · `Arabic Name` · `Rep Type` (= `Culture`) · `Number of labels` · `Duration (Day)`/`(Hour)` · `Frequence Check (Day)`/`(Hour)` · `Min Duration (Day)`/`(Hour)` · `Sample` · `Sample Condition` · `Culture Label Rep Type` · `Sex` · `Prefix` · `Volume` · ☐ `Biohazardous` · ☐ `Visible` · `Code` · `Culture Days` · `Stability` · `Precautions` · ☐ `Visible In Receipt`. 01:14:47

[NOTE:TIP] 🔑 What actually makes a mega a "culture"? `**

Neither the name nor the template. **A culture mega = an ordinary mega profile plus one test of type `Culture` inside it**, conventionally named `<sample> Swab Link`. That test is what makes the `Culture Entry` button appear when the patient's result is opened; without it the button stays away however you name the mega. The button is deliberately hidden from other tests so the results screen is not littered with controls that do not concern the user. 01:13:07

[NOTE:BAD] `A culture mega with no `Culture Entry` button 

A one-line diagnosis: no test of type `Culture` was added inside it. Open the profile inside the mega and check the `Type` column — at least one row must read `Culture`. Create it in `Setup ▸ Culture ▸ Culture Tests`, then add it to the profile.

[PROCEDURE]

#### `Building a complete culture — the reference case, `Urine Examination and Culture`

**ShapeOne mega profile holding **two profilesSource** مادة National Technology 2026-09-10

**`**`
1. **The first profile — the examination.** Name it `Urine Examination` and put in it all the **numeric and routine** tests of the ordinary work-up: the physical examination (`Color`, `Density`, `pH`) and the rest of the urinalysis items. These are ordinary tests, built as in 11.2 and 11.8. **
2. **The second profile — the culture.** First create the culture test itself in `Setup ▸ Culture ▸ Culture Tests`, then put it in a profile of its own. This is the test whose type is `Culture`. 
3. Create the mega `Urine Examination and Culture` and put both profiles in it, the examination profile first at `Order` 1. `
4. Set `Report Type` = `New Mega Culture`. `F10`.

**What you should see**`The report comes out ordered on its own: **the examination profile first**, then **the culture table directly beneath the tests**. A negative result (`No Growth`) prints **with no table**; a positive one prints **with the antibiotic sensitivity table**.

[PROCEDURE]

#### `Entering a culture result — the `Culture Entry` button

**Path** `Results By Accession No.` or `Results By Sample Id`

**Who does it** The microbiologist

`
1. Open the visit from `Results By Accession No.` or `Results By Sample Id` (chapter 08). The whole culture appears under its name, e.g. `Urine Examination and Culture`. **
2. Enter **the ordinary examination results first** on the main screen — colour, density, pH and the other numeric items. `**`
3. **At the foot of the screen, beside the `Last Result` button**, press `Culture Entry`. The culture detail screen opens. **`
4. Record the **causative organism** (`Organism`) — what grew. **`**`
5. Record the **antibiotics** and each one's **sensitivity**. **`
6. Write the culture **comment** if one is needed, then save.

**What you should see**`**`The culture table shows in the preview beneath the examination tests. If **the `Culture Entry` button is not there at all**, the fault is in the setup, not the entry — see "A culture mega with no `Culture Entry` button" above.



### 11.12 · Text reports and templates — `Text Tests` · `Text Test Templates`

The text report is for tests whose result is written paragraphs, not numbers: histopathology, cytology, cytogenetics. No reference range, no shading, no formulas — just headings, paragraphs and fonts. 01:16:00

[FIGURE]  [IMAGE img/img-067.webp — شاشة Text Tests]

_Caption:_ `**`**The `Text Tests` screen.** Alongside the usual fields (`Group Name` · `Text Name` · `Report Name` · `Arabic Name` · `Rep Type` · the durations · `Sample Code` · `Sex` · `No. Of Labels` · `Test Days` · `Precautions` · `Stability`) there is a `Text` box for the default text, the `Code` and `Templates` buttons, and the paragraph grid at bottom left: `S. No.` · `Header` · `Font` · `S` · `B` · `I` · `U`. 01:16:43



Each row of the paragraph grid is one paragraph on the report: its number (`S. No.`), its heading (`Header`), and its font, size and styling. The chemist later types each paragraph's content under its heading.

[FIGURE]  [IMAGE img/img-068.webp — شاشة نتيجة نصية]

_Caption:_ **`**The text result-entry screen** (`CYTOLOGY REPORT`): a free-text box on the left, and three paragraphs on the right each with a heading, a body, a `Font` button and `+` / `−` buttons. Below: the template list and `Load Template` · `Spell checker` · `Print` · `Print logo` · `Preview` · `Result Attachment`, plus a `Results Sound Recorder` strip for attaching a voice recording to the result. 01:17:36

[FIGURE]  [IMAGE img/img-069.webp — شاشة Text Test Templates]

_Caption:_ `**`**The `Text Test Templates` screen** (from the `Templates` button or the menu directly): `Template Name` · `Template Text` · and a grid of `S. No.` · `Header` · `Font` · `S` · `B` · `I` · `U` · `Result`. 01:19:40

[NOTE:TIP] Why templates `**

Pathology reports repeat almost word for word, differing by a line or two. Instead of the pathologist retyping the whole thing each time, the typical result is saved as a template. On the result screen they press `Load Template`, a pop-up lists every template with a **checkbox** beside it; whatever is ticked drops its text into the result, and they edit what needs editing. The alternative — what people who do not know this screen actually do — is retyping the same page every day.

`

### 11.13 · A single-price package — `Panels`

[FIGURE]  [IMAGE img/img-070.webp — شاشة Panels]

_Caption:_ `**`**The `Panels` screen:** `Short Name` · `Panel Name` · a `Code` button · ☑ `Visible`, and a `Panel Tests` grid of `Shortcut` · `Group Name` · `Test Name` · ☐ `Visible`. 01:21:04

**

A panel gathers tests, profiles and mega profiles **across groups** with no logical tie between them: no `Order`, no `Sum`, no formula. Each item prints its own way on its own template. 01:20:52

[NOTE] The point is purely financial `**

In `Financial` (chapter 10) every test, profile and mega is priced separately — **and the panel is priced as one entity** at the offer price. The trainer's example: CBC at 100 + LFT at 50 + ALT at 20 = 170 if ordered separately, and 50 as one panel. That is the mechanism behind offers and packages. A panel also has a `Code` so it can be ordered by code, and it appears in the `Groups` grid on `Patient Tests` as its own pseudo-group named `Panels`. 01:21:52

`

### 11.14 · Auto-add rules — `Test Rules`

[FIGURE]  [IMAGE img/img-071.webp — شاشة Test Rules]

_Caption:_ `The `Test Rules` screen:** `Rule Name` · `Group` · the `Rule` box · the `Test Name` list · the `+` · `>>` · `C` · `Check Rule` buttons. The saved rule here: `{D-dimer:}+{Cross-RH}>>{Eosinophils#}+{Factor IX:}+{Fibrinogen Degradation Product:}`, with the status bar reading `Data has been saved successfully.` 01:24:35

[PROCEDURE]

#### Writing a rule that adds tests alongside tests

**Path** `Setup > Tests Setup > Test Rules`

**Syntax** `{مُحفِّز}+{مُحفِّز} >> {يَنزل}+{يَنزل}`
1. Type a `Rule Name` and pick a `Group` — the list below shows that group's tests. `
2. Tick the triggering test in the list and press `+` — it is inserted into the `Rule` box in braces. Repeat for each further trigger; `+` means "and", i.e. both must be ordered together. 
3. Press `>>` to place the separator, then tick the tests that must be added automatically and press `+` for each. 
4. Press `Check Rule` to validate the rule logically before saving — it catches contradictory rules. `C` clears the box to start over. 
5. `F10`. The status bar reads `Data has been saved successfully.`

**What you should see`Order the triggers together in `Patient Tests` and the linked tests drop into `Selected Tests` on their own, **each at its own price**, and `Total Required` rises by their total.

[NOTE] When to use it 

When one test cannot be read without another (a result with no reference means nothing), and when reception must not forget a mandatory companion test. Do not use it to build a priced package — that is what `Panels` is for (11.13) — nor to compute a result — that is `Formula` (11.5).

`

### 11.15 · Entry shortcuts — `Tests Shortcuts`

[FIGURE]  [IMAGE img/img-072.webp — شاشة Tests Shortcuts]

_Caption:_ `**`**The `Tests Shortcuts` screen:** `Short Name` = `ICU`, with a `Tests` grid of `Shortcut` · `Group Name` · `Test Name`. In front of it the `Tests` picker with `a%` in `Find` — `%` is the wildcard, and the status bar reads `Choices in list: 161 (Choices in full list: 991)`. 01:26:05

[FIGURE]  [IMAGE img/img-073.webp — استعمال الاختصار في Patient Tests]

_Caption:_ `Using it on `Patient Tests`.** The `Groups` grid holds the groups, among them `Panels` and `Additional Services`; the `Shortcut` box sits at top right alongside `Total Required`, `T. Count`, `Discount`, `Disc. Per. %` and the `Money` · `Labels` · `Back` · `Tests` buttons. 01:27:09

|  | `Panel` | `Tests Shortcut` |
| --- | --- | --- |
| What it is | One catalogue entity | A typing shortcut, not an entity |
| Price | One package price | Each test at its own price |
| Group | `Shows under the `Panels` group | Each test lands under its own group |
| Editing after ordering | One block | Remove or add any item freely |



In use: the clerk types `ICU` into the `Shortcut` box and the whole package drops into `Selected Tests` at once with status `Ordered`, then they double-click away whatever was not requested and add whatever is missing. Hospitals benefit most: the intensive-care package is ordered every day with the same make-up, as is any client with a repeating standard request. 01:25:35

[NOTE:TIP] `The `%` wildcard in every picker `

LDM's pickers accept `%` as a wildcard: `a%` = everything starting with a, `%glucose%` = everything containing glucose. The status bar tells you the hits against the total (161 of 991) — use it to confirm you really narrowed the search.

`

### 11.16 · Unified codes — `Unique Services Codes`

[FIGURE]  [IMAGE img/img-074.webp — شاشة Unique Services Codes]

_Caption:_ `The `Unique Services Codes` screen:** `Code` · `Name` · `Group Name` · `Test Name` · `LOINC` · `CPT` with a per-row `Update` button. Examples from the environment: `105-7 house call dr mohamed bahae` · `90-6 panel1` · `137-7 Reprint result`. 01:28:48



This screen ties every catalogue service — test, package or additional service — to a unified internal code and to two international standards: **LOINC** (the international standard for naming lab observations) and **CPT** (the procedure code used in billing and insurance). Without them neither a hospital system (HIS) nor an insurer can make sense of what you sent. Fill it in if the lab integrates with anyone outside; leave it if the lab stands alone. 01:28:37

[NOTE] Not opened in this session 

`Setup ▸ Tests Setup ▸ Fonts ▸` is a submenu for system-wide fonts; the trainer never opened it. ⚠️ [Its contents need confirmation] — do not confuse it with the `Fonts` button at the foot of `Tests`, which styles a single test (11.2). Six of the `Setup ▸ Culture` screens were likewise never opened.

What needs confirmation in this chapter** (also collected in chapter 21): the rights for the `Tests Setup` screens · the practical difference between `Test Sum` and `Profile Sum` · the `Case Sums Chapter` and `Small` fields, never seen on screen · the literal name of the text report template · whether `Frequency Check` catches orders across branches · the contents of `Fonts ▸` · `Last Result` versus `Urgent` on the `Tests` screen · the six other `Setup ▸ Culture` screens (`Antibiotics` · `Culture Antibiotics` · `Culture Organisms` · `Organisms` · `Organisms Antibiotics` · `Sensitivity`) that were never opened · the details of `Formula Setup` and its literal equation syntax.

---

<!-- section c12 -->

## 12Users & privileges — who sees whatComplete

**

Every screen in this guide is preceded by one question: **is this member of staff allowed to open it at all?** The answer is written in two screens only — `Groups And Privileges` and `System Users` — and they are the source of every "you do not have permission" message a user ever sees. The chapter speaks in the voice of the **application engineer** who sets privileges once at installation and edits them at the lab manager's request. Every hh:mm:ss points into the **session 10** recording.

[NOTE:WARN] The menu name is misspelt in the product `**

The menu is called `Adminstiration` in the product — spelt exactly like that, not `Administration`. Inside it sits an item called `Rights Adminstiartion`, misspelt a second way. This guide prints them **as they appear on screen** so you can find them by eye, not as they ought to be spelt. 00:00:48

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 12.1 | `Why the group comes before the user — and a map of `Adminstiration` | Application engineer |
| 12.2 | `Creating a group — the `Groups And Privileges` screen | Application engineer |
| 12.3 | `Screen privileges — `QIUD` and `Special Rights` | Application engineer |
| 12.4 | Result privileges — `Groups and Branches` and `S R V E P` | Engineer + lab manager |
| 12.5 | Locations and ranks — `Locations` and `Print Results For Ranks` | Application engineer |
| 12.6 | `Creating a user — the `System Users` screen | Application engineer |
| 12.7 | `Signing the report — `Automatic Signature` | Engineer + doctor |
| 12.8 | The propagation rule, `Restore` and `Refresh Setup` | Application engineer |

`

### 12.1 · Why the group comes before the user — and a map of `Adminstiration`

**`

Picture a lab with ten reception staff. You could open each one's screen and tick their privileges by hand ten times — and get it wrong on the eleventh. The right way: build one **group** called `Reception`, set its privileges once, then join all ten to it and they inherit the lot. 00:00:53

[NOTE:TIP] Step zero — before you open any screen **

Sit with the **lab manager** and ask: how do you divide your team, and what may each team see, edit and print? That answer is your list of groups. Anyone who starts ticking boxes before that conversation will do the work twice. 00:02:08

[FIGURE]  [IMAGE img/img-075.webp — قائمة Adminstiration]

_Caption:_ `The `Setup ▸ Adminstiration` menu**, verbatim: `Groups And Privileges` · `Menu Management` · `Rights` · `Rights Adminstiartion` · `System Setup Tables` · `System Messages` · `System Objects` · `System Parameters` · `System Users` · `Monitor Sessions`. Only the first and the ninth concern this chapter. 00:00:48

`

### 12.2 · Creating a group — the `Groups And Privileges` screen

[FIGURE]  [IMAGE img/img-076.webp — شاشة Groups And Privileges]

_Caption:_ `The `Groups And Privileges` screen.** Top: `Group Name` (red = mandatory) · `Description` · ☑ `Visible`. Four tabs: `Privileges and rights` · `Groups and Branches` · `Locations` · `Print Results For Ranks`. On the left of the first tab, `Privileges` with an `Object name` column and `Q I U D All` boxes; on its right, `Special Rights` with a `Right name` column and an ☐ `Enabled` box. The status bar reads `Data has been saved successfully`. 00:03:11

[PROCEDURE]

#### Creating a new privilege group

**Path** `Setup > Adminstiration > Groups And Privileges`

**Right** System administrator. ⚠️ [Needs confirmation: the right's name]

**Prerequisites** The team split agreed with the lab manager · medical groups and branches already defined

`
1. Open the screen, then `F6` for a new record. 
2. `Group Name`: name the team, not the person — `Reception` · `Chemists` · `Answer`. This is the name you will pick later on the user screen. `**
3. `Description`: a description of the group. **Optional** — you may leave it blank, but fill it in if the lab has several similar groups. 00:02:48 `
4. Leave ☑ `Visible` ticked. Untick it to retire a group without deleting it. `**
5. `F10` to save ⇐ **the lower half of the screen appears** with its four tabs. Before saving you cannot set any privilege. 00:03:11
6. Set the four tabs in order: 12.3, then 12.4, then 12.5.
Expected result:** a saved group with a complete set of privileges, ready for users to be joined to it in 12.6.

`

### 12.3 · Screen privileges — `QIUD` and `Special Rights

The `Privileges and rights` tab has two halves. The left half, `Privileges`, lists every screen and menu in the system under the `Object name` column; against each one sit four rights and an "all" box. 00:03:55

| Box | Short for | What it allows inside the screen |
| --- | --- | --- |
| `Q` | `Query` | Search and query — to open the screen and pull its data |
| `I` | `Insert` | Entering a new record on the screen |
| `U` | `Update` | Updating data that is already there |
| `D` | `Delete` | Deleting a record from the screen |
| `All` | — | Ticks all four at once |

[PROCEDURE]

#### Granting the group a privilege on one screen

**Path** `Groups And Privileges ▸ Privileges and rights`

**Prerequisites** The group is saved (12.2)

`
1. Press `F7` for query mode. If you are in the middle of an unsaved edit the product asks whether to save — say yes. `**`
2. Type the screen's name into `Object name`. **Part of the name is enough** if you cannot recall it in full — type `Worklist`, say. `
3. Press `F8` ⇐ every screen carrying that word appears. 00:06:01 `
4. Tick what you want: `Q` and `I` only for staff who enter but do not edit, or `All` for someone who owns the screen outright. `
5. `F10` to save. 
6. To get the whole list back: `F7` then `F8` with no criterion ⇐ every screen returns in alphabetical order, so you can scroll by letter and tick as you go. 00:06:25
Expected result:** any user in this group opens the screen and can do only what you ticked; anything you left clear is greyed out or refused.

`**`

The right half, `Special Rights`, is not about screens but about **particular actions**: a `Right name` column and an ☐ `Enabled` box for each. Visible in the training build: `Add_Tests` · `Change Password` · `Change Username` · `Delete Test Any Day` · `Delete and Active verified test` · `Discount` · `Expire User Password` · `Listen Test Result` · `Modify Verified Result` · `Modify Vip` · `Payment` · `Preview_Without_Verify` · `Print All Bills` · `Print Invoice` · `Print Vip`. 00:06:40

[NOTE:TIP] The rights you will be asked about most **

`View Verified Result` and `View Reviewed Result`: may this member of staff see a result that has **not yet been reviewed or verified**? For reception, usually not. `Vip`: may they see the cases flagged VIP on the registration screen at all? `Modify Vip`: may they edit that data? Every right explains itself by its name on screen — read it before you tick it. 00:07:21



### 12.4 · Result privileges — `Groups and Branches` and `S R V E P`

[FIGURE]  [IMAGE img/img-077.webp — تبويب Groups and Branches]

_Caption:_ `**`**The `Groups and Branches` tab.** Left, `Groups` with a `Medical Groups` column (`ALLERGY` · `Body Fluids` · `BONE MARROW` · `CHEMISTRY` · `COAGULATION` · `CYTOGENAT` · `DRUG ABUSE` · `HAEMATOLOGY` · `HORMONES` · `IMMUNOLOGY` · `MICROBIOLOGY` · `PATHOLOGY` · `SEROLOGY` · `TUMOR MARKERS` …); right, `Branches` with a `Branches` column. Each row on both sides carries the same `S R V E P All` boxes. 00:10:00

[NOTE:WARN] **These five are about **results** and nothing else `

The trainer's words: "these shortcuts are tied to the `Result`, not to anything else." Do not read them as general privileges over the medical group — they are strictly about the test result: who sees it, reviews it, verifies it, edits it, prints it. 00:10:24

| Box | Short for | On the results of this medical group / branch |
| --- | --- | --- |
| `S` | `See` | May see the result |
| `R` | `Review` | May review it |
| `V` | `Verify` | May verify it |
| `E` | `Edit` | May edit it |
| `P` | `Print` | May print it |



What this buys you: the haematology chemist sees and verifies `HAEMATOLOGY` results only and never opens `CHEMISTRY`; a branch clerk sees their own branch's results and not the other's. The system supports several branches, and each branch holds a set of medical groups. 00:09:39



### 12.5 · Locations and ranks — `Locations` and `Print Results For Ranks`

[FIGURE]  [IMAGE img/img-078.webp — تبويب Locations]

_Caption:_ `**`**The `Locations` tab:** a `Location Name` column and an ☑ `Enabled` box. The training build carries three locations: `In Patient` · `Out Patient` · a dialysis-units entry. Tick whichever this group may work on. 00:11:25

`**

The fourth tab, `Print Results For Ranks`, answers a single question: whose **ranks** may this group print results for? Either named ranks or all of them. It is used in military and government hospitals where cases are split by rank. 00:11:25

`

### 12.6 · Creating a user — the `System Users` screen

[FIGURE]  [IMAGE img/img-079.webp — شاشة System Users]

_Caption:_ `**`**The `System Users` screen.** Left: `Real name` · `Password` · `Group` ▾ · ☑ `Modified` and a `Restore` button. Middle: `User name` · `Confirm Password` · `Expire Date` · `Period`. Right: ◉ `Locked` / ◉ `UnLocked` · ☑ `Password Expire` · ☐ `Change Password` · ☑ `Visible` · a `Shifts` column. The same four tabs sit beneath. 00:13:20

[PROCEDURE]

#### Creating a user and joining them to a group

**Path** `Setup > Adminstiration > System Users`

**Prerequisites** The group is ready with its privileges (12.2–12.5)

`
1. `F6` for a new record. To pull up an existing user for editing instead: `F7`, your search criterion, then `F8`. 00:13:07 
2. `Real name` the person's real name · `User name` their login · `Password` and `Confirm Password` matching. `**
3. `Group`: pick the group ⇐ **they inherit all of its privileges automatically**. This is the step that saves you ticking dozens of boxes. 00:15:18 
4. ◉ `UnLocked` so the user can work, or ◉ `Locked` to bar them temporarily without deleting them. **
5. ☑ `Password Expire` if the user is temporary ⇒ **`Expire Date` becomes mandatory**. Example: a one-year contractor ⇒ enter the contract's end date. After it, they are told at login that the password has expired and cannot get in. 00:13:22 
6. ☐ `Change Password` lets the user change their own password from `Tools ▸ Change password`. 00:13:43 
7. Leave ☑ `Visible` ticked for an active user. `Shifts` is optional — the person's shifts (`Night` · `Morning` …), whose values come from another screen; most labs do not use it. 00:15:31 `
8. `F10` to save. Then — only if you must — adjust their privileges individually from the four tabs, and read 12.8 before you do.
Expected result:** a user who logs in with their own name and password and finds their group's screens — and no others.

[NOTE:INFO] Period` — needs confirmation `

⚠️ [Needs confirmation: `Period`] The recording is not conclusive; most likely it is a number of days within which the password must be changed. Do not rely on it before checking with the product team. 00:14:23

`

### 12.7 · Signing the report — `Automatic Signature`

The doctor who verifies results does not sign a hundred reports by hand every morning. Instead their signature is stored as an image on their user record and the system prints it onto the report. 00:16:06

[FIGURE]  [IMAGE img/img-080.webp — اختيار صورة التوقيع]

_Caption:_ Picking the signature image.** The user record carries a preview box with a `Browse` button under it, which opens an `Open` dialog to choose the image file. The same frame shows the `Locations` tab and the `User name` · `Confirm Password` · `Expire Date` · `Period` fields with ◉ `UnLocked`. 00:16:40

[PROCEDURE]

#### Adding a doctor's signature to the report

**Path** `System Users ▸ Browse`

**PrerequisitesThe signature scanned and saved as a **JPG** on the server machine · the report template set up to read it

**
1. Scan the doctor's signature and save it as a **JPG** file. 00:16:27 
2. Open the doctor's record in `System Users` and press `Browse`.
3. Pick the JPG ⇐ the image appears in the preview box. `**
4. `F10` to save. **The signature will not appear on the report** unless the report template is set up to look for it and print it. 00:17:14
Expected result:** every report this doctor verifies comes out bearing their signature, untouched by hand.

[NOTE:INFO] Image size

⚠️ [Needs confirmation: the size] The recording says the image has a suitable size that must be set so it prints clearly, but gives no number. Test it on a real report before handover. 00:17:30



### 12.8 · The propagation rule, `Restore` and `Refresh Setup`

[NOTE:BAD] The rule people forget, and wreck a whole team's privileges 

Change a privilege on the **group** ⇒ it takes effect at once for **every** user joined to it. Change a privilege on **one user** ⇒ it touches neither the group nor its other members. So if you mean to change one person, change their own record; anyone who edits the group to satisfy a single member of staff has just changed it for the whole team without realising. 00:09:02

`**`

And when you have added to or trimmed a user's privileges away from their group and want to undo it: press **`Restore`** on their record ⇒ they revert to their group's privileges and every individual change of yours is dropped. The ☑ `Modified` box beside it is what tells you this user has drifted from their group. 00:14:56

[NOTE:WARN] `Branches — `Refresh Setup**`

Any setup under `System` — users and privileges included — is **done on the main branch** that holds the setup. For it to reach the other branches you must run `Refresh Setup`. If someone sets privileges on a sub-branch and then wonders why nothing happened, this is why. 00:13:56

---

<!-- section c13 -->

## 13Analyser integration — from the machine to the result screenComplete

`

With no integration, the chemist reads the number off the analyser's screen and types it into the system by hand — and every hand-copy is a chance to get it wrong. This chapter removes that step: four screens under `Setup ▸ Communication` define the machine and map its codes to your test codes, then a Windows service carries the messages between them. The chapter speaks in the voice of the **application and integration engineer**. Every hh:mm:ss points into the **session 10** recording.

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 13.1 | The four communication modes and three physical links | Everyone |
| 13.2 | Defining a machine — `Analyzers`, the `General` tab | Application engineer |
| 13.3 | `Port settings — the `Comm` tab | Integration engineer |
| 13.4 | `Mapping the tests — the `Analyzers Tests` screen | Application engineer |
| 13.5 | Ranges, units and reshaping the result | Engineer + chemist |
| 13.6 | `Sample codes — the `Analyzer Samples` screen | Integration engineer |
| 13.7 | `Cultures — the `Analyzer Culture Tests` screen | Engineer + microbiologist |
| 13.8 | The config file and encrypting the database details | Integration engineer |
| 13.9 | `Installing the service with `sc create` | Integration engineer |
| 13.10 | Reading the log when it breaks | Support |

### 13.1 · The four communication modes and three physical links

**

Before any screen: know how your machine talks. There are four modes, and the **machine** decides which — not you. You read it from the manufacturer's manual, then mirror it in the setup. 00:19:17

| Mode | Direction | How it works | Where it is set |
| --- | --- | --- | --- |
| `Uni-directional` | Machine → system | The machine takes no `Order` at all. You scan the `Sample ID` at the machine, which is programmed for a fixed test, it runs and sends the result. Most CBC analysers work this way. | `Leave ☐ `Query` clear |
| `Bi-directional` | Machine ⇄ system | `**`You load the sample, the machine reads the `Sample ID` and **asks** the system what is ordered for it; the system answers with an `Order message` listing the tests, the machine runs them and sends the results back. | `Tick ☑ `Query` |
| `Download` | System → machine, in advance | **The system **pushes** the data to the machine at registration time, before the sample arrives: the row is written to the `Download` table and the machine finds it waiting. The difference from `Query`: there it asks when the sample arrives, here it already has it. | `Tick ☑ `Load` |
| `Shared folder` | Effectively one-way | `The test is run on the machine, then the result is `Export`ed as an **XML** file or an **Excel sheet** into a folder the service watches; it picks the file up, parses it and posts the results. No messaging at all. Most PCR machines work this way. | `Query Location` · `Result Location` |

**`**

There are three physical links: **Serial**, a cable from the machine to the PC running the service; **Network / TCP-IP**, where your service is either `Client` or `Server` and **the machine is the opposite**, the two speaking `HL7` messages; and **File share**, the shared folder above. 00:24:53

[NOTE:BAD] What really happens when you pick the wrong mode **

Set a machine to `Uni-directional` and the `Order` will never work: staff must load the sample and start the test **by hand on the machine**, and only then is the result sent automatically. A second point: if the sample orders ten tests and the machine holds five of them, **some** machines run the five and ignore the rest, and **some refuse the sample entirely**. That difference comes from the machine, not from your setup. 00:21:53

[FIGURE]  [IMAGE img/img-081.webp — قائمة Communication]

_Caption:_ `**The `Setup ▸ Communication` menu**, verbatim: `Analyzers` · `Analyzers Tests` · `Analyzer Samples` · `Analyzer Culture Tests`. Four screens only — **and their order is the order of work**: define the machine, map its tests, then its samples, then its cultures if it is a culture analyser. 00:26:50



### 13.2 · Defining a machine — `Analyzers`, the `General` tab

[FIGURE]  [IMAGE img/img-082.webp — شاشة Analyzers تبويب General]

_Caption:_ The `Analyzers` screen — `General` tab.** Columns: `Code` · `Name` · `Default Sample` · `Default Type` · `Default Test Name` · `Copies` · ☐ `Auto Print` · ☐ `Load` · ☐ `Append UnReg Tests` · ☐ `Append UnReg Patients` · ☐ `Update R Result` · ☐ `Update V Result` · ☐ `Conf`. The training build holds three machines: `1 CD1800 EDTA` · `2 Kx21 EDTA` · `3 ILAB Serum`. 00:33:40

[PROCEDURE]

#### Defining a new analyser on the system

**Path** `Setup > Communication > Analyzers`

**Right** The `Analyzers` privilege in `Groups And Privileges` (12.3)

**Prerequisites** `The manufacturer's manual · the tests already defined in `Tests Setup` (11)

`
1. `Name`: the machine as you know it — `KX21` for a `Sysmex KX21`. 00:27:02 `**`
2. `Code`: in most builds it is **generated automatically** after `F10`; in some the field is open and you type it. Note it down — you will need it in the config file (13.8). 00:27:06 **
3. `Default Sample`: the sample type this machine mostly runs — `EDTA` for a CBC analyser. **Optional**: if the machine has no usual sample, leave it blank; nothing breaks. 00:27:47 
4. `Default Type`: which kind of catalogue item it runs — a single `Test`, a `Profile`, or a `Mega`. (See the seven types in 11.1.) 00:28:00 `**
5. `Default Test Name`: fill this only if the machine runs **one test and nothing else** — a CBC-only analyser, say. 00:29:19 `**
6. `Copies`: number of printed copies — **leave it at 1**. 00:29:30 
7. Tick the boxes according to the table below, `F10` to save, then move to the `Comm` tab (13.3).
Expected result:** a new row in the analyser list with a fixed code, ready for its tests to be mapped.

| Box | What it does |
| --- | --- |
| `Auto Print` | **`Once the result lands, the report prints **automatically**. It is tied to the auto-verify module: the machine sends ⇐ `Auto-verify` approves ⇐ the printer produces the report. Deployed at Alexandria University. 00:29:33 |
| `Load` | Runs the machine in `Download` mode: the moment a case is registered its row is written to the `Download` table for the machine to find. 00:30:22 |
| `Append UnReg Tests` | **`If the chemist adds a test at the machine that was **never ordered** for the sample, the machine sends it and the system registers it automatically. Example: your `KX21` is defined for CBC only, yet it accepted another test. 00:30:53 |
| `Append UnReg Patients` | **`The same logic at **patient** level: a sample with a `Sample ID` and patient data but no registered test ⇒ registered automatically. The payoff is a report showing everything that came straight off the machine, so the lab can see how many samples bypassed registration. 00:32:24 |
| `Update R ResultUpdate V Result` | **`**`May the machine overwrite a result that has already been **reviewed** or **verified**, when the sample is re-run. 00:32:41 |
| `Conf` | `**`⚠️ [Needs confirmation] The on-screen hint reads `To flag if this device is device or not`. **The trainer said outright that he does not know exactly what it means and that they do not use it**; the likeliest reading is that the machine flags itself as `Confirmatory` or not. Do not tick it without a reason. 00:33:04 |

`

### 13.3 · Port settings — the `Comm` tab

[FIGURE]  [IMAGE img/img-083.webp — شاشة Analyzers تبويب Comm]

_Caption:_ `The `Comm` tab.** Columns: `Comm Setting` · `Comm Port` · `Form Name` · `Text File` (`In / Out`) · `Query Location` · `Result Location`. The training rows: `4800,n,8,1 | 3 | cd1800` · `4800,n,8,1 | 1 | Kx21` · `9600,n,8,1 | 4 | ilab350`. 00:34:03

[NOTE:WARN] These numbers are not invented `

`Comm Setting` is the **baud rate** plus serial settings in the form `<baud>,n,8,1` — commonly `4800,n,8,1` and `9600,n,8,1`, with `115200` also in use. **It comes from the machine's manual or its own settings screen** and is copied across character for character. A wrong number here means total silence on the line. 00:34:14

[PROCEDURE]

#### `Finding the `Comm Port` number in Windows

**Path** `This PC` ▸ right-click ▸ `Manage` ▸ `Computer Management` ▸ `Device Manager` ▸ `Ports`

**When** `Before filling `Comm Port` for any serial machine


1. Older PCs had a real serial port on the case ⇒ usually `COM1` or `COM2`. 00:35:15 
2. Modern machines have no serial port ⇒ a **USB-to-serial** converter is used, and its port number **varies** with whatever Windows assigns. 00:35:23 
3. Open `Device Manager` by the path above and read the number under `Ports`. 00:36:04 `**
4. Type that same number into `Comm Port` here, **and into the config file later** (13.8) — the two must match.
**`

**Expected result:** the service opens the right port; get it wrong and the log carries an `Exception` saying the port does not exist (13.10).

`**`**`

As for `Form Name`, it is a legacy of the old system: until about two years before the recording, integration was done with **Oracle Developer 6i forms** (`.fmb` compiled into `.fmx`) built by a dedicated engineer, and the form's name went here — `cd1800` · `Kx21` · `ilab350`. **The current direction is the Windows Service**, not forms (13.9). `Text File`, `Query Location` and `Result Location` serve the shared-folder mode. 00:36:55

[NOTE:INFO] A contradiction in the recording 

⚠️ [Needs confirmation: `Form Name` with the service] The recording says once that `Form Name` is still required even with a Windows Service, and once that it belongs to the old forms alone. Check with the integration team before relying on either. 01:00:10

`

### 13.4 · Mapping the tests — the `Analyzers Tests` screen

**

The machine does not know your test names. It has a **code** for each test; you have a name. This screen is the translator between them: "that code on the machine = this test of mine." Without the mapping, no message is understood in either direction. 00:39:43

[FIGURE]  [IMAGE img/img-084.webp — شاشة Analyzers Tests]

_Caption:_ `The `Analyzers Tests` screen.** Top: an `Analyzer` field with `Print All` · `Print Analyzer` buttons. Columns: `Group` · `Test Type` · `Test Name` · `Visible` · `Analyzer Code` · `Host Code` · `R.S` · `Slope` · `Intercept` · `Cut Off` · `Unit` · ☐ `Query` · ☐ `Query Details` · ☑ `Auto Send` · and `…` buttons for `Details` · `Reference Range` · `Methodology` · `Result`. Note the `Kx21` codes — `HCT` · `HGB` · `LYM` · `MCH` · `MCHC` · `MCV` · `MXD` · `NEUT` · `PLT` · `RDW` · `RBC` · `WBC` — and that `Auto Send` is ticked on every configured row. 00:40:40

[NOTE:BAD] `☑ `Auto Send` — without it, not one result is saved `

The single most important box on the screen. The trainer's words: "**I must tick it, so that when the machine produces a result it actually lands in the LDM. If I do not tick it, it will not land. `Auto Send` is critical — it is the Saving Result**." It is the **permission to save the incoming result**: every bit of mapping, querying and port setup before it counts for nothing while this box is clear. If a customer complains that results "never arrive" although the log looks clean — check this box first. 00:45:03

[PROCEDURE]

#### Mapping a machine's tests to its codes

**Path** `Setup > Communication > Analyzers Tests`

**Prerequisites** `The machine defined (13.2) · the list of test codes from its manual · the tests present in `Tests Setup
1. In the `Analyzer` field pick the machine — `F9` brings up an `Analyzer Name` list of every machine you have defined. `**
2. Press `F8`. If the machine has never been set up, the screen comes back **empty** for you to start entering. 00:40:12 `
3. Pick the `Group` (`HAEMATOLOGY`, say) ⇐ the setup follows automatically, then pick the `Test Name`. `
4. Set `Visible` (`Y` / `N`) for this test. `**
5. Enter the `Analyzer Code` — the test's code **exactly as it is on the machine**. 00:40:35 `**`
6. Enter the `Host Code`. On most machines it is **identical** to the `Analyzer Code` (as in the training rows). On some it differs: the code it sends in a result is one thing, the one it expects as Host when querying is another — sometimes just a serial number. 00:41:20 `
7. Tick ☑ `Query` if the machine is `Bi-directional`; leave it clear for `Uni-directional`. 00:43:53 `**
8. **Tick ☑ `Auto Send`** — otherwise no result is ever saved (see the warning above). `
9. `F10` to save, and repeat for every test the machine runs.
Expected result:** every result the machine sends lands against the right test on the results screen.

[NOTE:INFO] ☐ `Query Details` and the `Details` button — for a test that returns several values `**`**`

Sometimes a test is **one** item for you while the machine returns several sub-values for it **under codes different from the parent's**. The example: `Coagulation` analysers — your `PT` is a single test, and the machine returns `INR`, `Control time` and `Concentration` alongside it, "and the LDM has no such sub-division." **What to do:** tick ☑ `Query Details` on the parent test, then open the `Details…` button on the same row and enter the sub-values' codes as the machine has them. So the `Details` button **belongs to `Query Details`** and means nothing without it. 00:44:19 ⚠️ [Needs confirmation: no frame of the `Details` dialog itself was captured]

`**`**

The two print buttons at the top: `Print Analyzer` prints **this machine's** list with its codes, and `Print All` prints the list for **every** machine defined. A useful sheet to pin up beside the analyser. 00:48:19

### 13.5 · Ranges, units and reshaping the result

`

The remaining columns of `Analyzers Tests` handle the gaps between what the machine emits and what your report wants.

| Column / button | What it solves |
| --- | --- |
| `Reference Range…` | **`Some labs want a **different** reference range for this machine than for another running the same test. Enter it here ⇒ every result from this machine carries its own range. The same dialog sets `Numeric` / `Non-numeric`. **Priority rule:** the system looks at the **machine's** range first; if it is blank it falls back to the general `Test Setup` range. 00:45:27 |
| `Methodology…` | `**The machine's technical method. Write it here and it appears **next to the `Analyzer Name` on the result report** — provided the template is set to show it. 00:46:12 |
| `Slope` | **`**The machine reports in a unit **you do not carry** in `Test Setup` (the example given: `PT`). `Slope` is a number the incoming result is **multiplied by** to convert it to your unit — "divide by ten, or multiply by a thousand, or by a hundred." 00:42:15 |
| `R.S` | ``**`Result Selection`. Some machines send **more than one result for the same test** in a single message, separated by `/`. `R.S` is the **position number** of the one you want: `1` for the first, and so on. ⚠️ The trainer notes this is now set **on the service side** rather than here. 00:49:33 |
| `Unit` | **The unit for this machine's result specifically — **"we do not use this one."** 00:43:13 |
| `Intercept` · `Cut Off` | **⚠️ [Needs confirmation] Their explanation was deferred to a later session and "we do not use them much." By the names, `Intercept` would be the constant in `y = Slope·x + Intercept` — **an inference, not a statement**. 00:48:10 |

[FIGURE]  [IMAGE img/img-085.webp — حوار RESULT]

_Caption:_ ``**The `RESULT` dialog.** Opened from the `Result…` button on the test's row. A `Results` section with three columns: `Operator` ▾ · `Value` · `Return`. The frame shows `>` with `10`. It turns the incoming number into **text** on the report — for instance, anything under 10 prints as `Negative`. Operators: greater than · less than · equals, and several rows may be entered to build a full conversion table. 00:47:45

`

### 13.6 · Sample codes — the `Analyzer Samples` screen

**

Some machines **refuse to run a sample at all** unless the sample-type code arrives inside the message. This screen maps your sample type to its code on the machine. 00:50:37

[FIGURE]  [IMAGE img/img-086.webp — شاشة Analyzer Samples]

_Caption:_ `**`**The `Analyzer Samples` screen.** An `Analyzer` field at the top (here `Kx21`) and a `Samples` grid of two columns: `Sample` and `Analyzer Sample Code`. The visible example: `Urine` ⇐ `u`. 00:52:10

`**

Machines named as needing this mapping: `Cyst-a-riff` · `E911` · `C311` · `A880` · `C811`. The codes always come from **the machine's manual or its own settings screen**; for any new machine, ask the manufacturer for the manual. 00:51:10

[NOTE:WARN] Codes change with the `Kit` or the `Lot number` **`

A test's code on the machine is not permanent: it can change when the kit or the lot number changes. So **train the customer to edit the code themselves** from `Analyzers Tests` rather than waiting on the vendor each time. 00:52:31

`

### 13.7 · Cultures — the `Analyzer Culture Tests` screen

**

An ordinary analyser sends "test ⇒ one value." A culture analyser does not: it sends an **organism**, **antibiotics** and a **sensitivity**. Each of those carries its own code on the machine, which is why this screen has a tab for each. 00:54:15

[FIGURE]  [IMAGE img/img-087.webp — شاشة Analyzer Culture Tests]

_Caption:_ `**`**The `Analyzer Culture Tests` screen.** An `Analyzer` field at the top and four tabs: `Culture` · `Antibiotics` · `Organisms` · `Sensitivity`. The `Culture` tab's columns: `Culture Name` · `Analyzer Code` · `Analyzer Code2`. 00:54:45

**

Mapping is done by `Culture Name`: `Urine culture` · `CSF culture` … each with its code on the machine. The antibiotics are then mapped on their tab, the organisms on theirs, the sensitivity on its own. The best-known machine here is **`Vitek`** from bioMérieux. 00:53:30

[NOTE:INFO] Where you will actually meet these machines **`**

Culture analysers are expensive and live only in **large labs and university hospitals**. The trainer, across his time at the company, connected a `Vitek` **once**, at Ain Shams hospital. Do not expect one at every installation. 00:55:04 ⚠️ [Needs confirmation: `Analyzer Code2` — the difference from `Analyzer Code` was never explained]

### 13.8 · The config file and encrypting the database details

**`**

The system side is done; the other end remains: the **service** that carries the messages. The integration team hands you a folder with a `<name>.exe`, some supporting files and a **config file** — and that file is the critical step. 01:00:33

[FIGURE]  [IMAGE img/img-088.webp — مجلد الخدمة]

_Caption:_ **`**The service folder** — example `analyzers\Celltac_G\Celtac_G\Comm`: `CelltacF` (the application) · `CelltacF.exe` (the `CONFIG File`) · `CelltacF.InstallLog` · `CelltacF.InstallState` · `CelltacFFrameWork.dll` · `CommonLibrary.dll` · `Entities.dll` · `Oracle.ManagedDataAccess.dll` · `tcpServer.dll`. 01:02:35

[NOTE:BAD] **Set the file **before** installing **`

Any wrong value in the config file ⇒ the service **fails to start** once installed. Set it completely first, then install. 01:01:08

[FIGURE]  [IMAGE img/img-089.webp — ملف الإعداد]

_Caption:_ The config file** and its keys: `ConnectionString` (server IP · database name · user · password), then `AnalyzerName` · `AnalyzerId` · `CommunicationType` · `CommunicationSettings` · `CommunicationPort` · `TcpIp` · `TcpPort` · `MessageRate` · `LogFilePath`. Note the in-file comments: `SerialPort = 0, TcpClient = 1, TcpServer = 2` and `Mandatory in case of Serial`. 01:01:45

| Key | What goes in it |
| --- | --- |
| `AnalyzerNameAnalyzerId` | `Must match the `Name` and `Code` you defined on the `Analyzers` screen (13.2). A mismatch means the service writes for a machine that does not exist. |
| `CommunicationType` | `**`0` serial · `1TcpClient` · `2TcpServer`. Read from the machine's manual: if the machine is the `Server`, your service is the `Client`, and **vice versa**. |
| `CommunicationSettingsCommunicationPort` | **`Mandatory **for serial only**: the baud rate and the COM port number — the same values you put on the `Comm` tab (13.3). |
| `TcpIp` · `TcpPort` | **For TCP: the machine's address and port. **Network condition:** machine and server must be on one network and able to see each other; otherwise there is no connection however you configure it. 01:05:11 |
| `MessageRate` | The message rate, usually left as is; some machines need `500` or `200`. 01:04:45 |
| `LogFilePath` | `The log folder's path (13.10). Some machines need extra folders: `backup` for incoming sheets · `result` for the sheet the service pulls · `failed` for whatever did not go through. 01:06:31 |

[PROCEDURE]

#### `Encrypting the `ConnectionString`

**Why** The config file is plain readable text — any IT person at the customer can open it and see the server IP, database name, user and password

**Tool** `Encryptor` · `Decryptor`
1. Copy the `ConnectionString` text from `DATA SOURCE` up to the semicolon after the password. 01:02:19 
2. Open `Encryptor` and paste the text into `Text To Encrypt`. 
3. Press `Encrypt` ⇐ the result appears in `OutPut`.
4. Put that result in place of the original text in the config file and save. 
5. **To edit later:** the integration team now ships the file **already encrypted**. So copy the encrypted text ⇒ `Decryptor` ⇒ edit ⇒ `Encryptor` ⇒ put it back. 01:12:33


**Expected result:** the service runs exactly as before and nobody can read the database details out of the file. Encryption is **technically optional** — the service works without it — but in practice it is a required security step.

[FIGURE]  [IMAGE img/img-090.webp — أداة Encryptor]

_Caption:_ `**`**The `Encryptor` tool** (window title `Form1`): an `Encrypt` section with `Text To Encrypt`, an `Encrypt` button and an `OutPut` box. 01:02:55

`

### 13.9 · Installing the service with `sc create`

[PROCEDURE]

#### Installing an analyser communication service on Windows

**Prerequisites** `**The config file set and saved (13.8) · a `CMD` running as **Administrator**

`**
1. Open `CMD` **as Administrator**. Under any other user the command will not work. 01:07:46 **
2. Type: `sc create "<Service Name>" binPath= "<full path to the exe>"` — **mind the space after `binPath=`** and before the quotation mark. 01:08:51 `
3. `<Service Name>`: take the exe's own name. If you connect more than one machine of the same model to the same PC ⇒ `CelltacF1` · `CelltacF2`, and so on. 01:08:19 `
4. Success shows as `[SC] CreateService SUCCESS`. `
5. Open `Services` ⇒ the service is there with `Startup Type` = `Manual` by default. `
6. Change it to `Automatic (Delayed Start)` so it comes up after Windows boots ⇒ `Apply` ⇒ `Start`. 01:09:50
Expected result:** the service runs and starts writing a daily log. If it fails, the cause is in the config file — open the log (13.10).

[FIGURE]  [IMAGE img/img-091.webp — نافذة Services]

_Caption:_ `**`**The `Services` window** showing the new `CelltacF` service with `Startup Type` = `Manual`, beside the Oracle services `OracleServiceLDM` · `Oracle LDM VSS Writer Service` · `OracleJobSchedulerLDM`. Behind it, the `CMD` with the `sc` command and its path ending in `CelltacF.exe`. 01:09:45

[NOTE:INFO] `The alternative — `Oracle Analyzer Installer

There is a one-window tool that replaces editing the file by hand and running `sc create`: `Analyzer ID` · `Analyzer Name` · `Log File Path` · `Comm Type` · `Comm Port` · `Comm Setting` on the left, `DB Server` · `DB Name` · `DB User Name` · `Password` · ☐ `Local DB` on the right, then `Application EXE Path` with a `Browse` button, ☑ `Start Service`, a `Log` box and an `Install` button.

[FIGURE]  [IMAGE img/img-092.webp — أداة Oracle Analyzer Installer]

_Caption:_ `**`The `Oracle Analyzer Installer` tool.** ☑ `Local DB` is ticked when the database sits on the same machine. **The trainer advises against using it** for two stated reasons: it puts the database credentials on an exposed screen in front of whoever is standing beside you, and the config file carries extra settings the tool does not cover. The supported path is 13.8–13.9. 01:15:44

### 13.10 · Reading the log when it breaks

`**`**

The service writes a log file into `LogFilePath` named for the date — and **one file per hour**, as a real log folder shows (chapter 14), alongside a parallel `__Error_` file. The log is **encrypted**; open it with the `Decryptor` tool run as Administrator and it appears as `<name>[Decrypted]`. 01:10:27

[FIGURE]  [IMAGE img/img-093.webp — اللوج بعد فك التشفير]

_Caption:_ The log after decryption.** In order: `SecurityCheck | Initialize Communication` → `InitializeDevicePortSettingAndLog | Success | [Id=5, CommSetting=9600,n,8,1, COM=4]` → `ReadFromSerialAndLog | In | …` with the raw message (showing the machine model `MEK-8222` and `CBC + Diff`) → `NewSaveBulkResult | ServiceEvent | Begin SaveResult: [SID=…]` → one line per test, `StartSend: SaveResult: [SID=… TCode=… R=… Flag=…]`. Note the `*` · `L` · `H` flags. 01:11:30

[PROCEDURE]

#### Diagnosing an integration fault from the log

**Who does it** Support, from a log the customer's IT sends in

**`
1. Ask the customer's IT for the log of **the day the problem happened** and open it with `Decryptor`. 01:20:00 
2. Check the `InitializeDevicePortSettingAndLog` line: are the `Id`, the machine name, the `CommSetting` and the `COM` number all right? A config mistake shows up here immediately. 01:11:23 `
3. Look for an `Exception`. The common one: the port named in the file does not exist because the machine is not plugged in. 01:11:08 **
4. On the happy path every save appears twice: `StartSend: SaveResult` then **`Passed Check: SaveResult`** — and the second means it went through cleanly. (Corrected from the PCR recordings’ frames; see chapter 14.) 01:12:05
5. Raise a ticket with what you found instead of guessing the cause from the customer's description.
Expected result:** the fault is pinned down from a single file, with no site visit.

[NOTE:INFO] Where the product is heading **

Instead of one service per machine per branch, the stated direction is **a single central service** (Web LDM) that all branches look at — to ask less of the customer. Not yet built at the time of recording. 01:20:12

What needs confirming in this chapter** (also collected in chapter 21): `Conf` on the `General` tab · `Form Name` with a Windows Service · the shape of the `Details` dialog · `Intercept` and `Cut Off` · where `R.S` is really set · whether `Auto Print` is checked against `Medical Rules` · `Analyzer Code2` on the culture screen.

---

<!-- section c14 -->

## 14PCR integration — from a gene's Ct to a saved resultComplete

**_**

Every machine in chapter 13 hands you a **finished result** and the service carries it across unchanged. A PCR machine does not: it hands you **a number per gene per well**, and the verdict — positive, negative or invalid — is computed **inside the service** before anything is saved. That is why a PCR interface needs two files, not one, and one service per _machine + kit_, not per machine. The chapter speaks in the voice of the **integration engineer** and extends chapter 13 rather than replacing it. Every PCR1 mm:ss or PCR2 mm:ss points into one of the two PCR recordings.

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 14.1 | Why PCR is unlike everything before it | Everyone |
| 14.2 | The two variables: the `run file` and the `interpretation sheet` | Integration engineer |
| 14.3 | `Reading a `run file` — the BioRad CFX example | Integration engineer |
| 14.4 | The same thing on another machine — QuantStudio 5 | Integration engineer |
| 14.5 | The per-gene threshold — the first half of the interpretation sheet | Engineer + chemist |
| 14.6 | The overall verdict — four marks into one result | Engineer + chemist |
| 14.7 | **One service per machine **and** kit — naming and folders | Integration engineer |
| 14.8 | The config file — the PCR-specific keys | Integration engineer |
| 14.9 | `Following a file: `Results` → `Backup` or `Fail`? | Support |
| 14.10 | The log: decrypt it, then check a result by eye | Support |

### 14.1 · Why PCR is unlike everything before it



PCR detects a virus or a bacterium through its **DNA or RNA** — specifically through **named genes**. The gene is bound to a dye and the machine measures how much dye is there: more target gene, more bound dye, a bigger detection number. PCR1 00:15

**

In the lab the sample goes through three stages before the machine: **extraction** of the organism you are looking for ⇐ **amplification** ⇐ then into the machine's **wells**, where the dye stage and the detection stage run. PCR1 01:04

[NOTE:INFO] What that means for the integration engineer **

The machine gives you **no result**. It gives you **a Ct value per gene per well**. The service is what turns those values into `DETECTED`, `NOT DETECTED`, `Presumptive positive` or `INVALID` — **before** it saves anything. That is the one real difference from a chemistry or haematology analyser. PCR1 01:43 · PCR2 11:02



### 14.2 · The two variables: the `run file` and the `interpretation sheet`

Every PCR interface rests on exactly two variables, and each one has a file: PCR1 01:58

| # | Variable | The file | Decides |
| --- | --- | --- | --- |
| 1 | The machine** (its software) | `run file` | Where** each value sits: which sheet, which column, which row |
| 2 | The kit** | `interpretation sheet` | What** each value means: the per-gene threshold, then the overall verdict |

**

And note before anything else: most PCR machines are integrated by **file transfer** (text, Excel or CSV), not by a serial or TCP conversation — the fourth mode in 13.1. PCR1 02:14

[PROCEDURE]

#### Getting both files before you configure anything

**Who does it** Integration engineer

**When** Before the service is created

`**`**`
1. **The `run file`:** when the machine is installed, the vendor's application engineer runs a **`validation run`** — the machine's QC/calibration — and it produces a sheet in that day's data which serves as the `run file`. PCR2 00:18 `**`
2. Or simply ask the lab for a **run file exported** from the machine's own software. PCR2 00:46 `
3. **The `interpretation sheet`:** request it from the **kit provider** (or the machine provider), saying plainly that you need it to build the interface. It comes back as a soft copy. PCR2 00:57
4. Open both before writing a single line of config. The first tells you where to cut the values from; the second tells you what to do with them. PCR1 04:33
Expected result:** two files on your machine — with those two alone you can write the whole service without a second visit to the lab.

[FIGURE]  [IMAGE img/img-094.webp — مجلد PCR session]

_Caption:_ The working folder** holds everything you need: `interpretation` (the kit's interpretation sheet) and a `run file` per machine — here `NEOPLEX-REPEAT-522-6-04-07-2022` from a QuantStudio and `SOLGENT-519-80-03-07-2022- - Quantifi…` from a BioRad. PCR1 02:44

`

### 14.3 · Reading a `run file` — the BioRad CFX example

**

A BioRad file has **two sheets**: `Run Information` (the run's own data) and `0` (the sample data). PCR1 02:38

[FIGURE]  [IMAGE img/img-095.webp — شيت Run Information]

_Caption:_ `**`**`**The `Run Information` sheet.** `File Name` · `Created By User` (the user logged into the machine's software) · `Run Started`/`Run Ended` · `Sample Vol` 20 · `Lid Temp` 105 · `Protocol File Name` · `Plate Setup File Name` · `Base Serial Number` · **`Optical Head Serial Number` = `785BR29753`** · `CFX Manager Version`. PCR1 03:08

[NOTE:TIP] Optical Head Serial Number` is the security lock **`**`

This number **identifies the machine**. Put it in the `Parameter` key of the service's config file (14.8) and the service will **only ever accept files produced by that one machine**. You can see it happen in this chapter: the same `785BR29753` appears in the sheet and in the config file. PCR1 03:08 · PCR2 05:45

[FIGURE]  [IMAGE img/img-096.webp — شيت بيانات العينات]

_Caption:_ `**`**Sheet `0` — the sample data:** `Well` · `Fluor` · `Target` · `Content` · `Sample` · `Cq` · `Cq Mean` · `Cq Std. Dev`. `Content` separates `Unkn` (a patient sample) from `Pos Ctrl` (`Sample` = `PC`) and `Neg Ctrl` (`NC`). PCR2 02:07

| Column | What you take from it |
| --- | --- |
| `Well` | The position on the plate: `A01`…`H12` on a 96-well plate. The order is either vertical (`A1, A2, A3…`) or horizontal (`A1, B1, C1…`) — and that same ordering becomes a fault if someone changes it later (14.9). PCR2 02:07 |
| `Fluor` ← `Target` | The dye and the gene it works on. For this kit: `VIC`←`ORF1a` · `CY5`←`RdRp` · `FAM`←`N Gene` · `Cal Red 610`←`IC`. PCR2 02:56 |
| `Sample` | The `Sample ID` the result is saved against — it is what later appears as `SID=` in the log. PCR2 01:49 |
| `Cq` | `**The value the whole interpretation runs on. Blank or `0.00` means **no amplification** — the gene is not there. PCR2 03:27 |

### 14.4 · The same thing on another machine — QuantStudio 5

This is the section that explains why there is no one-size-fits-all PCR service: the same information, in a completely different shape. PCR1 04:09

[FIGURE]  [IMAGE img/img-097.webp — run file من QuantStudio]

_Caption:_ ```**A QuantStudio 5 run file with a NeoPlex kit.One sheet** (`Results`), not two, and the run header sits **above** the sample data in that same sheet: `ChemistryTAQMAN` · `Experiment File Name` · `Instrument TypeQuantStudio™ 5 System` · and **`Instrument Serial Number`** in place of `Optical Head Serial Number`. Data columns: `Well` · `Well Position` · `Sample Name` · `Target Name` · `Reporter` · `CT` · `Ct Mean` · … · `Amp Status`. PCR1 04:19

| Difference | BioRad CFX + SOLGENT | QuantStudio 5 + NeoPlex |
| --- | --- | --- |
| Sheets | Two: `Run Information` + `0` | `One: `Results`, header above the data |
| Security number | `Optical Head Serial Number` | `Instrument Serial Number` |
| Value column | `Cq` | `CT` |
| Gene name | `Target`: `ORF1a` · `RdRp` · `N Gene` · `IC` | `Target Name`: `Neo-IC` · `Neo-N gene` · `Neo-RdRp` (three genes, not four) |
| "No result" token | `0.00` | `Undetermined` with `Amp Status` = `No Amp` |

_

So as the integration engineer you are literally telling the service: _"take the text at column so-and-so, put its value in this field; take the `Sample ID` from here; take the gene name from here; take the `Well` from here"_ — which is exactly why all of it changes when the machine changes. PCR1 04:49 · PCR2 01:35

### 14.5 · The per-gene threshold — the first half of the interpretation sheet

**

Interpretation happens in **two steps**: first each gene on its own — positive, negative or borderline? — then all the genes together ⇒ the overall verdict. PCR2 11:30

[FIGURE]  [IMAGE img/img-098.webp — ورقة التفسير]

_Caption:_ The SOLGENT kit's interpretation sheet.** Each column header carries its own threshold: `VIC (Ct reading) ORF1a Gene` · `CY5 … RDRP` · `FAM … N gene` · `CAL RED … IC`, and under it `Positive (+) </= 38` · `Negative (-) N/A or >40` · `PP >38 - <=40`. The last column, `Covid 19 Interpretation ▸ Result`, lists every combination and its verdict. PCR1 05:56

| Dye | Gene | `Positive `+` | Borderline `BL`/`PP` | `Negative `−` |
| --- | --- | --- | --- | --- |
| `VIC` | `ORF1a gene` | Ct ≤ 38 | > 38 – ≤ 40 | `N/A` or > 40 |
| `CY5` | `RDRPs gene` | Ct ≤ 38 | > 38 – ≤ 40 | `N/A` or > 40 |
| `FAM` | `N gene` | Ct ≤ 38 | > 38 – ≤ 40 | `N/A` or > 40 |
| `CAL RED` | `IC` (internal control) | Ct ≤ 40 | none | `N/A` or > 40 |

[NOTE:BAD] Correction: the spoken rule is inverted — the sheet is the reference **

In the recording the rule is spoken as: "whatever comes out smaller than 40 ⇒ negative, and greater than or equal to 38 ⇒ positive", with an example reading `ORF 18 = negative` and `N gene 45 = positive`. **That is backwards** (and self-contradictory: `<40` negative and `≥38` positive overlap). PCR1 07:46

`

**The correct rule — what the screen itself says and what the service's log confirms:a low value is positive**. `Ct ≤ 38` ⇒ positive · `>38` and `≤40` ⇒ borderline · `0`, `N/A` or `>40` ⇒ negative. The proof is in the log: sample `B03` with `35.17 · 34.63 · 33.33` was written as `R=DETECTED`, and sample `A02` with zeros as `R=NOT DETECTED`. The trainer states it correctly later himself: "I have a result up to 38, per the interpretation sheet". PCR2 13:06

### 14.6 · The overall verdict — four marks into one result



Once each gene is a `+`, a `−` or a `BL`, the service looks that combination up in the interpretation sheet and takes its `Result`. The rule that comes out of the grid: PCR1 06:12

| Combination | Result on screen | In the log |
| --- | --- | --- |
| Two or more** of the three target genes positive | `Detected (Positive)` | `R=DETECTED` |
| `Only one gene positive, or any `BL` present | `Presumptive Positive` | `R=Presumptive positive` |
| No positive and no `BL`, with the `IC` present | `Not Detected (Negative)` | `R=NOT DETECTED` |
| **`**All four negative** — the `IC` included | `INVALID` | — |

`**`

**Why four zeros is `INVALID` and not "negative":** the `IC` is the internal control. If even it did not come up, the run detected nothing at all — not the virus, not its own control — so it says nothing about the patient. PCR1 06:55 · PCR2 13:30

[NOTE:WARN] Two contradictory rows in the same sheet 

The combination `ORF1a +` · `RdRp −` · `N −` · `IC +` is written **twice** in this sheet with two different verdicts: once as `Not Detected (Negative)` and once as `Presumptive Positive`. **Ask the kit provider which row governs before building a service on this sheet** — what hangs on it is whether a patient is retested or cleared. Listed in chapter 21. PCR1 08:40

**

### 14.7 · One service per machine **and** kit — naming and folders

`**`**

The `run file` is fixed per machine, but the interpretation changes per kit. So one BioRad running three kits needs **three services**, each named **`<machine>_<kit>`**. PCR2 04:18

[FIGURE]  [IMAGE img/img-099.webp — مجلد Analysers]

_Caption:_ ``D:\COMM\Analysers\` on a real customer server:** `Biorad_Solgent` · `QS_Berkinelmer` · `QS_Neoplex` · `QS_Solgent` — one QuantStudio with three kits and one BioRad with one kit: four services for two machines. PCR2 09:18

[FIGURE]  [IMAGE img/img-100.webp — مجلد الخدمة]

_Caption:_ `Inside `Biorad_Solgent`:** a `log` folder · `FastDetection_Freiburg.exe` (the service) · `FastDetection_Freiburg.exe.config` (the `CONFIG File` — everything you edit) · `ExcelDataReader.dll` and `ExcelDataReader.DataSet.dll` (reading the Excel) · `Oracle.ManagedDataAccess.dll` · `CommonLibrary.dll` · `Entities.dll` · `tcpServer.dll`. PCR2 05:12

[NOTE:WARN] A stated limitation with no shown solution **

The machine's software exports to **one path**. So if you run more than one kit on the same machine and point the software at a single path, both kits' files land in one folder and neither service can read the other's. The trainer names the problem and shows no way round it. PCR2 07:43

### 14.8 · The config file — the PCR-specific keys

**`

The file is the one described in 13.8: a `ConnectionString` encrypted with `Encryptor`, then `AnalyzerName` and `AnalyzerId`, which **must** match the `Analyzers` screen. What is new here is three folder keys and one security key. PCR2 05:12

[FIGURE]  [IMAGE img/img-101.webp — ملف الإعداد]

_Caption:_ ``FastDetection_Freiburg.exe.config`.** Note `AnalyzerName` = `Biorad_Solgent_1` · `AnalyzerId` = 4 · `LogFilePath` · `CommunicationType` = 2 · `Parameter` = `785BR29753` · then the three that matter here, `ResultFolder` · `BackupFolder` · `FailedFolder` · and `AnalyzerType` = `Oracle`. PCR2 05:32

| Key | Value in the example | What goes in it |
| --- | --- | --- |
| `Parameter` | `785BR29753` | The machine's unique number** as read off the `run file` (14.3): the `Optical Head Serial Number` on a BioRad. With it the service works for that machine only. PCR1 03:40 |
| `ResultFolder` | `D:\COMM\Results\Biorad_Solgent_1\` | **The in-tray.** The machine's software exports here and the service reads from here. A **shared** folder (14.9). PCR2 06:49 |
| `BackupFolder` | `D:\COMM\Results\Backup\` | **Where the service moves a file **after reading and saving it successfully**. PCR2 05:32 |
| `FailedFolder` | `D:\COMM\Fail\` | **Where a file the service **could not read** goes, after its retries. PCR2 05:42 |
| `LogFilePath` | `D:\COMM\Analysers\Biorad_Solgent\log\` | The log folder — and the log is encrypted (14.10). |
| `CommunicationType` | 2 | `The in-file comment explains it: `SerialPort = 0, TcpClient = 1, TcpServer = 2`. The `CommunicationSettings` and `CommunicationPort` keys are ignored unless the link is serial. |

`

### 14.9 · Following a file: `Results` → `Backup` or `Fail`?

[FIGURE]  [IMAGE img/img-102.webp — مجلد COMM]

_Caption:_ `**`**`D:\COMM\`** holds exactly three folders: `Analysers` (the services) · `Fail` · `Results`. PCR2 06:05

[FIGURE]  [IMAGE img/img-103.webp — مجلد Results]

_Caption:_ `**`**`D:\COMM\Results\Biorad_Solgent_1\` is empty** — which is the healthy state: everything dropped in it has been picked up. Note the status bar at the bottom of the window: `State: Shared`. PCR2 06:49

[PROCEDURE]

#### Sharing the in-tray and pointing the machine's software at it

**Who does it** Integration engineer with the customer's IT

`**
1. Share the `ResultFolder` to **the PC or laptop that runs the analyser's software** — that is the mandatory one. PCR2 07:04 **
2. Sharing it to the rest of the PCR room is **optional** and depends on the lab. PCR2 07:05 `
3. Better still: `Map network drive` on that PC, then set the machine's software to export to that path every time — so nobody moves a file by hand. PCR2 07:26 **
4. **Careful:** do not do this if you run more than one kit on the same machine — see the warning in 14.7. PCR2 07:43
Expected result:** every run ends with a file landing in the in-tray automatically, with no human step.

[PROCEDURE]

#### Tracing a result that never appeared

**Who does it** Support

**Input** A complaint: "today's run did not come into the system"

**
1. Open the `ResultFolder` and `Refresh`. **The file disappearing** means the service picked it up. PCR2 08:02 `**
2. Look for it in the `BackupFolder`. Found ⇒ **it was read correctly** and the problem is elsewhere; move on to the log (14.10). PCR2 08:19 `**
3. Found in the `FailedFolder` ⇒ **it was rejected**. Start with the three questions below. PCR2 08:24 **
4. **(a) Is the file even for this service?** A QuantStudio file dropped into the BioRad service's folder: one sheet against two, different columns ⇒ it cannot be read. PCR2 06:15 **
5. **(b) Did someone rename a gene** in the machine's software? The file then carries a gene name the service does not know. PCR2 08:34 **
6. **(c) Did the column order or the well sequence change?** For instance the export was vertical, `A1 A2 A3`, and is now horizontal, `A1 B1 C1`. PCR2 08:47
Expected result:** the file's location alone separates "the service never read it" from "the service read it and the problem is downstream" — before you open a single log.

### 14.10 · The log: decrypt it, then check a result by eye

[FIGURE]  [IMAGE img/img-104.webp — مجلد اللوج]

_Caption:_ **The log folder** holds two families: `Biorad_Solgent_1_2022-06-08 03.txt` (the normal log) and `Biorad_Solgent_1__Error_2022-05-27 07.txt` (the error log) — **one file per hour**, not per day. Note the sizes: the error files are 11 MB per hour on 26–27 May and 1 KB afterwards — the directory listing alone tells you when this interface was broken. PCR2 09:34

[FIGURE]  [IMAGE img/img-105.webp — أداة Analyzer Decryptor]

_Caption:_ `The `Analyzer Decryptor` tool.** ◉ `Import Text File` ⟵ a `Path`, an `Import` button and ☑ `Open Text After Decrypt?` for a whole file; or ○ `Insert Text` ⟵ paste one line into `Encrypted Text` and read it in `Plain Text`. The output is written beside the original as `<name>[Decrypted].txt`. PCR2 10:06

[FIGURE]  [IMAGE img/img-106.webp — اللوج بعد فك التشفير]

_Caption:_ The log after decryption.** Each line reads: `22:16:06.612` ‖ `NewSaveBulkResult` ‖ `SecurityCheck` ‖ `StartSend: SaveResult: [SID=… TCode=… R=… Flag=… ]`. Every result appears **twice**: `StartSend` and then **`Passed Check`** — and it is the second that says it actually landed. PCR2 10:44

[NOTE:INFO] A correction to chapter 13 

Chapter 13 said every save ends with `past checked`. The text on screen here is **`Passed Check`**, and it has been corrected there. Likewise, the log is written **hourly**, not daily. PCR2 10:44

[FIGURE]  [IMAGE img/img-107.webp — حقل Flag في اللوج]

_Caption:_ `**`**`**The `Flag` field is the audit trail**, and you build it in the service field by field: `<Well>-<Gene>-<Ct>` per gene, separated by `\\\`, then **the name of the `run file` the values came from** (`SOLGENT-263-64-06-06-2022 - Quantification Cq Results`), then `Comment = @@Comment` · `ModuleCode =` · `Units=@@Units` · `RefRange…`. PCR2 12:09

[PROCEDURE]

#### Checking a saved result by eye, from the log

**Who does it** Support / integration engineer

**Why** To prove the service is interpreting correctly — without even opening the application

**You need** The day's log + the kit's interpretation sheet

`
1. Decrypt the relevant hour's log with `Analyzer Decryptor` and find the `SaveResult` line carrying the sample's `SID=`. PCR2 10:17 `
2. Read the four gene values out of `Flag`. A real one: `A02-ORF1a-0` · `A02-N Gene-0` · `A02-RdRp-0` · `A02-IC-34.47`. PCR2 12:45 
3. Judge each gene against the thresholds in 14.5: the three zeros ⇒ negative, and the `IC` at `34.47` ≤ 40 ⇒ positive. PCR2 13:03 `
4. Apply the rule in 14.6 ⇒ `Not Detected`. Compare with the `R=` on the same line: `R=NOT DETECTED`. They agree ⇒ the service is sound. PCR2 13:13 `**`
5. Confirm there is a **`Passed Check`** line for the same `SID`. A `StartSend` with no `Passed Check` ⇒ the result never landed. **
6. **The last check:** four zeros including the `IC` must come out `INVALID`. If it comes out as anything else, the service is interpreting wrongly — "a result appeared" is not good enough. PCR2 13:30
**`

**Expected result:** a documented verdict on the interpretation from a single file, with the name of the `run file` every value came from.

| `SID` | `ORF1a` | `N Gene` | `RdRp` | `IC` | `R=` |
| --- | --- | --- | --- | --- | --- |
| `QDOC0001671(well `A02`) | 0 | 0 | 0 | 34.47 | `NOT DETECTED` |
| `QDOC0001549(`B02`) | 0 | 0 | **26.65** | 31.06 | `Presumptive positive` |
| `QHC0003074(`B03`) | **35.17** | **34.63** | **33.33** | 30.44 | `DETECTED` |
| `QHC0003060(`B04`) | **34.6** | **34.71** | **33.37** | 31 | `DETECTED` |



Four real lines from the `Biorad_Solgent_1_2022-06-06 22[Decrypted]` log — and on their own they settle which way the threshold runs: low values gave `DETECTED`, zeros gave `NOT DETECTED`, and one gene alone gave `Presumptive positive`. PCR2 12:45

**`**What needs confirming in this chapter** (also collected in chapter 21): the two contradictory rows in the interpretation sheet · how a lab running two kits on one machine with a single export path is actually set up · whether `Parameter` on a QuantStudio service holds the `Instrument Serial Number` · what `AnalyzerVersion` and `CommonVersion` select · whether the `_1` in `Biorad_Solgent_1` is an instance counter · how many read retries happen before a file is moved to the `FailedFolder`.

---

<!-- section c15 -->

## 15Auto Verification — releasing a result without a doctor's clickComplete

**

A chemistry or haematology unit can take **1000–1500 samples a shift**. Having the doctor walk every result one by one to press `Verify` is an unbearable load. The `Auto Verification` module releases a result **without the doctor pressing anything**, from rules and conditions you entered in advance — **and took from the lab itself**. The target stated in the training: the doctor ends up reviewing **200–300 patients** instead of 1500, so only the abnormal and the critical reach him. The chapter speaks in the voice of the **implementation engineer**. Every S13 mm:ss points into the Session 13 recording.

[NOTE:INFO] What this chapter covers and what it does not 

The session covers **setup and rule-writing**. How the **engine actually runs**, troubleshooting, and investigating an existing rule were explicitly deferred by the trainer to a following session that has not reached us. S13 01:02:50

### Procedure index for this chapter

| # | Procedure | Who does it |
| --- | --- | --- |
| 15.1 | The two halves: managerial before technical | Implementation engineer |
| 15.2 | Unique Services Codes` — the gate | Engineer + development team |
| 15.3 | Verify Setting` — the two tabs and the master switch | Implementation engineer |
| 15.4 | The seventeen checkers — what each one does | Implementation engineer |
| 15.5 | `Why `Normal check` stays off | Implementation engineer |
| 15.6 | `The delta family: `Delta check` and its siblings | Implementation engineer |
| 15.7 | Analyser flags — a flag that stops and a flag that does not | Implementation engineer |
| 15.8 | `Result Option` and `Result Dictionary` | Implementation engineer |
| 15.9 | `The `Medical Rules` screen and its keypad | Implementation engineer |
| 15.10 | The operator dictionary — every button and what it means | Implementation engineer |
| 15.11 | `H-Val` and `L-Val` — a percentage is a multiplication | Implementation engineer |
| 15.12 | `And`, `Or`, and the rule that stops what you never meant | Implementation engineer |
| 15.13 | `Old screen vs new — `Formula Condition` | Implementation engineer |
| 15.14 | LR`, `N.D.C` — and why `No LR` exists | Implementation engineer |
| 15.15 | Worked rules from the field | Implementation engineer |
| 15.16 | The editor, `Check Rule` and `Help` | Implementation engineer |

### 15.1 · The two halves: managerial before technical



When a new lab arrives the job is two halves: a **managerial half** where you sit with people, and a **technical half** between you and the development team. Do not start the second before finishing the first. S13 03:40

**
1. Go to the lab and find the **contact person** — the unit manager or the lab director. He is the source of the rules, not you. 
2. Sit with him knowing he **speaks medically, not technically**. He will say "release it when it's normal", or "pass it from 5 to 50". He does not know how to turn that into a rule — **translating medical into technical is your job**. **`
3. Collect the rules **per group**: Chemistry, Haematology, Immunology, Hormones, Microbiology. If he has them on an `Excel` sheet, take it as it is. **
4. Ask for **every unit's rules in one go**. Do not go back unit by unit ("I've finished Chemistry, now give me Haematology") — ask once and let him collect them all.

[NOTE:WARN] Roll out group by group — never all at once

If you got one test wrong in one group and you have enabled every group, that same mistake is now repeating across all of them and you must fix it everywhere. Going group by group keeps the headache off the doctor — and off you. S13 04:50

`

### 15.2 · `Unique Services Codes` — the gate

`

The first screen of the technical half, at `Setup ▸ Test Setup ▸ Unique Services Codes`. You ask the **development team** to bring in **every test in LDM** and put a code beside each one. S13 06:20

[FIGURE]  [IMAGE img/img-108.webp — شاشة Unique Services Codes]

_Caption:_ ``Unique Services Codes`.** Columns: `Code` · `Name` · `Group Name` · `Test Name` · `LOINC` · `CPT` · `Update`. The capture shows the `Bacteriology` group with its codes. S13 06:45

[NOTE:BAD] This screen is a gate: what is not in it does not exist in the rules `**_

The `Medical Rules` screen reads this one **first** to confirm the test's code exists. **If the test is not registered in `Unique Services Codes` it never appears in `Medical Rules` at all**, and you cannot write a rule for it. You can watch it happen: pick the group, `F9` at `Test`, choose the test, `Enter` — then `Enter` **a second time** to make the parameters appear in the left-hand box. If the code is not registered it answers that the _parameter / test does not exist_. S13 06:50 · S13 21:00

`

### 15.3 · `Verify Setting` — the two tabs and the master switch

`

At `Setup ▸ Verification Setup`, with two tabs: `General Settings` and `Units Settings`. S13 07:35

[FIGURE]  [IMAGE img/img-109.webp — تاب General Settings]

_Caption:_ ``Verify Setting ▸ General Settings`.** The `Enable Online Verification` checkbox and the `LMP Preparation No.` field. S13 07:40

`**

`Enable Online Verification` is the **master switch**: turn it on and every incoming result runs through the engine and is released or stopped automatically, and the doctor no longer depends on the manual `Result check` screen. `LMP Preparation No.` was never explained in the session (21.1).

[NOTE:BAD] Do not tick this box at the beginning _

The trainer says it plainly: when you start a lab, **do not enable the engine**. Two reasons, the second far graver: **(1)** you cannot yet tell whether the rules you entered are correct. **(2)a result that should have stopped would go out to the patient** before you have checked it. So the order is: enter the rules, verify them, _then_ enable the engine. S13 08:00

### 15.4 · The seventeen checkers — what each one does

**

On the `Units Settings` tab you pick a group from `Groups` and tick the checkers you want **for that group alone**. This is where "group by group" actually happens. S13 11:30

[FIGURE]  [IMAGE img/img-110.webp — تاب Units Settings]

_Caption:_ `Verify Setting ▸ Units Settings` for the `Chemistry` group.** Ticked in the demo: `Medical check` · `Flag Check` · `Main Rules check` · `Dev Flag Check` · `No Rules`. Note that `Normal check` is deliberately off (15.5). S13 12:01

[NOTE:TIP] The two most important `**`**

The trainer names them: **`Medical check`** — the one that sends the engine into your rules in the first place — and **`No Rules`**, the safety net that stops a test with no rule from passing silently. S13 12:40

| Checker | What it does |
| --- | --- |
| `Medical check` | `The core. Sends the engine to the `Medical Rules` screen to apply that test's rule. |
| `No Rules` | **_**Any test registered in the group with **no rule** raises a _No Rule_ message and its result **does not pass**. |
| `Result check` | `Checks the result value itself against a mathematical sign (`=`, `>`, `<`). |
| `Comment check` | `Compares the written comment against `Result Dictionary`; a mismatch raises an error (15.8). |
| `Option check` | `Checks the result against the allowed values in `Result Option` (15.8). |
| `Flag Check` | **Any flag on the result stops it — **unless** it is registered as non-stopping (15.7). |
| `Dev Flag Check` | **Checks the flag sent by **the analyser itself**. |
| `Incomplete check` | `Any result still `incomplete` (no value yet) stops the sample. |
| `Panic check` | Any panic result stops; the reportable range stops too. |
| `Normal check` | ⚠ Deliberately left off — 15.5. |
| `Delta check` | The result's change from the previous one over a period — 15.6. |
| `All Group Complete` | **A test is verified only when **every** test in its group is complete — 15.6. |
| `Delta check Analyzer` | **The variance **between two analysers** for the same sample — 15.6. |
| `Main Rules check` | **The trainer said "we will not explain this — a modification we do not use", although it is **ticked** in the demo (21.3). |
| `Missing Result check` · `Quality Control check` · `Online Verification` · `Reportable check` | Present on the screen, never explained in the session (21.2). |

`

### 15.5 · Why `Normal check` stays off

[NOTE:BAD] Normal check` silently overrides every widening you wrote into your rules `

This checker stops **anything outside the reference range**, **regardless of your rule**. The trainer's example says it all: albumin's reference range is 3.5–5, and in `Medical Rules` you wrote that it may pass **from 3 to 6**. A result of **3** arrives. Your rule says release it. It **stops anyway** — because 3 is outside the reference range. You conclude your rule is broken, when in truth this one box is overriding it. **Leave it unticked.** S13 13:00

`

### 15.6 · The delta family: `Delta check` and its siblings

**`

The values for these three are **not written here** — they come from `Test Setup`, and these boxes merely switch them on. S13 16:41

**`Delta check`**  Releases a result only if its change from the previous one stays inside a percentage over a period. **Example:** ALT is allowed **+60%** over **30 days**. The patient registers on day 1 at 50, returns 20 days later at 150. The engine reads the delta: did it rise by ≥60% inside the 30 days? It did — **so it stops**. S13 17:04

**`All Group Complete`  A modification working at **group** level. With it on, a test is verified only when **every** test in its group is complete: a chemistry group of some 15 tests must have all 15 results. If even one is missing, **the engine does not run at all** for that sample and raises an error that the group must be complete — nothing is released. S13 18:12

**`Delta check Analyzer``**  The same idea but **between machines**, for labs with more than one. Bilirubin runs on the Sysmex and returns 0.5; the sample is re-run on another machine and returns 2. The engine measures the variance between the two against a percentage set in `Test Setup` (say 40% or 50%), and if it is exceeded reports that **this analyser is off, or this result has a problem**. S13 19:11

### 15.7 · Analyser flags — a flag that stops and a flag that does not

`**_

With `Flag Check` on, **any** flag stops the result. In live use the doctors said: _no, some flags are acceptable to us_. Hence the modification. S13 14:20

[FIGURE]  [IMAGE img/img-111.webp — شاشة Analyzers ونافذة Flags]

_Caption:_ `Analyzers` with its `General` and `Comm` tabs, and the `Flags` popup over it** with `Flag Code` and `Flag Name`. In the analyser list: `ADVIA1800` · `CX9` · `Dimension` · `ADVIA_1650` · `CA1500` · `Axsym` · `ACCESS2` · `ESR` · `GEN_S` · `Max_M` · `HMX` · `CX9_2` with codes 20–31. S13 15:29


1. Open `Analyzers`, go to the `Comm` tab, and press the `Flag` button (`..`) beside the analyser. 
2. Enter the `Flag Code` and `Flag Name` for each flag the machine sends. 
3. The checkbox beside the flag is the decision: **ticked = this flag stops the result**; **unticked = the flag is merely recorded and does not stop it**. Medically acceptable flags are entered here and left unticked.


### 15.8 · `Result Option` and `Result Dictionary`

Two screens that spare you rules which are hard to write. S13 08:50

[FIGURE]  [IMAGE img/img-112.webp — شاشة Result Option]

_Caption:_ ``Result Option`:** `Group` · `Test` · the `Option Name` list · a `Print` button. The capture shows group `Parasitology` and parameter `Colour`. S13 09:30

``Result Option`** is for any parameter whose result is **a choice, not a number** — the trainer's example is `Colour` in urine: open the screen, pick the `Group`, `F9` at `Test` and choose the parameter, enter the options allowed to pass (`Negative`, say), then `F10` to save. **Why the screen exists at all:** because writing that into the rules (`colour like …`) is, in the trainer's words, very hard. Drive it with the `Option check` checker.

[FIGURE]  [IMAGE img/img-113.webp — شاشة Result Dictionary]

_Caption:_ ``Result Dictionary`:** `Word` · `Case Sensitive` · `Not Valid`, with the `Print valid words` and `Print all` buttons. The word entered in the demo is `Normal CBC`. S13 10:44

`**``Result Dictionary`** is for comments. With `Case Sensitive` ticked the comment must match the stored spelling **letter for letter**; any deviation stops the result through `Comment check`. `Not Valid` marks a word as unacceptable.

`

### 15.9 · The `Medical Rules` screen and its keypad

[FIGURE]  [IMAGE img/img-114.webp — شاشة Medical Rules]

_Caption:_ `**`**`Medical Rules`.** On the left `Group` = `Haematology`, `Test` = `CBC`, and the `Parameters` box of code-name pairs (`2843-RBC`, `2846-HGB`, `2849-PLT`…). In the centre the operator keypad with `Check Rule`, `Print` and `Print Help`. On the right the rules grid: `Code` · `Formula` · `Recommendation` · `Main Rule` · `Active` · `Help`. S13 22:12

**`

**To load a test:** pick the `Group`, `F9` at `Test`, choose the test and `Enter`, then `Enter` again to fill `Parameters`. If nothing appears, the test is missing from `Unique Services Codes` (15.2).

`

**Syntax:** a parameter is written **in braces** — `{TSH}`, `{HGB}`, `{PLT}`.

### 15.10 · The operator dictionary — every button and what it means

[FIGURE]  [IMAGE img/img-115.webp — لوحة المعاملات]

_Caption:_ `The complete `Medical Rules` keypad.** This is the entire vocabulary of the rule language; what is not on it cannot be written. S13 22:12

| Button | Meaning |
| --- | --- |
| `C` · `Back` | Clear · backspace. |
| `*/+-` | `Multiply, divide, add, subtract, for when a result must be combined arithmetically. `*` is the operator of the percentage formula in 15.11. |
| `><=` | Greater than · less than · equals. |
| `<>` | **`_Not equal.** The trainer's example: `{Creatinine} <> 0` — _every_ result passes **except** a result of 0, which stops. S13 22:50 |
| `And` · `Or` | Logical linking — 15.12. |
| `Not` | **⭐ Negates what follows. `norm{ALT}` releases a normal ALT; `Not norm{ALT}` releases when the result is **not** normal. Also used as `Not Prep50` and `Not Require`. S13 23:20 |
| `Norm` · `High` · `Low` | `Inside the reference range · any high result · any low result. Written `norm{TSH}`. |
| `H-Val` · `L-Val` | **⭐ The upper/lower **reference-range** value, read live — 15.11. |
| `Sex` | `sex=1` male · `sex=2` female. |
| `age` | **Age **in days by default**. For years, multiply: 13 years = `365*13`, 18 years = `365*18`. |
| `dev` | **`The **device code**, not its name; taken from the `Analyzers` screen. |
| `Doc` | The doctor's code. |
| `Prep` | **`**`**A **registration question by its code**: `Prep50` = question 50 answered **yes**, and `Not Prep50` = answered **no**. |
| `Require` | **`Another test **must be registered** alongside this one. `Not Require` is its opposite. |
| `LR` · `LR period` · `No LR` · `N.D.C` | The last result and its window, "no last result", and the normal delta check — 15.14. |
| `Comment` · `With` · `Like` | `**`Attaching a comment. The text goes **between two `#` hashes**: `High Urea with comment {Urea} like #High Urea#`. S13 30:30 |
| `Round` | Round the result value. |
| `Abs` · `Lowd` · `LMP` · `Then` · `desc1` · `desc2` · `desc3` · `loc` · `^` | Present on the keypad, never explained in the session (21.4). |



### 15.11 · `H-Val` and `L-Val` — a percentage is a multiplication

_**`**`**

The doctor says: _"release albumin if it is within ±20% of the reference range"_. You could type the range numbers by hand — but **reference ranges change from one analyser to another**, so a hard-coded value is a deferred bug. Hence the two buttons: `L-Val` reads the **lower** reference-range value as it currently stands, and `H-Val` reads the **upper** one. S13 32:00

[NOTE:BAD] ⭐ Never write the percentage as a subtraction — write it as a multiplication **

If you write "minus 20%" as a direct subtraction you can land on **zero or a negative number**, and then **everything below it passes** — which is plainly wrong. "20% lower" means **80% of the value**, i.e. **× 0.8**; "20% higher" means **× 1.2**. S13 33:20

Worked example — albumin, range 3.5–6, tolerance ±20%:**

`
{Albumin} > L-Val * 0.8   and   {Albumin} < H-Val * 1.2
`

**

The engine then tests 3.5 × 0.8 = **2.8** as the floor and 6 × 1.2 = **7.2** as the ceiling. A result of **4** sits inside both — so it passes.

[NOTE:TIP] If the doctor gives you an absolute value, not a percentage `

If he says "±1" or "±2", do not multiply — write it straight: `{Albumin} > L-Val - 2 and {Albumin} < H-Val + 2`. The trainer calls this "the easy case". S13 35:10

`Limit:** `H-Val`/`L-Val` work on the **numeric** range (`Reference Range From/To`) only; **an alphanumeric range is not covered**.



### 15.12 · `And`, `Or`, and the rule that stops what you never meant

| The two conditions | `And` | `Or` |
| --- | --- | --- |
| true · true | **true** | true |
| true · false | false | true |
| false · true | false | true |
| false · false | false | **false** |

`

With `And`, **every** condition must hold. With `Or`, the only way to get `false` is for **all** conditions to fail. S13 36:00

[NOTE:WARN] A condition absent for a patient = a rule that stops him 

This is the lesson the whole session keeps returning to. A rule saying "age under 13 and albumin above 1.7" — a 12-year-old arrives without those values and **the rule stops him**. The cure: add an `Or` arm for the other side of the boundary (`age > 365*13`) so the rule does not stop patients it was never written for. **Every boundary needs its complement.** S13 50:30

**_Why the trainer insists:** because _you_ control whether the linking is right — "you may think the rule is correct and it doesn't come back the way you want; check that you linked the conditions correctly". A rule returning the wrong verdict is usually a **linking error, not a value error**. So: if a rule grows large, break it into conditions, satisfy each one alone, then link them. S13 37:40

[NOTE:TIP] `Draft rules in `Notepad++` first `**

The trainer names it twice as working practice: `Notepad++` **matches brackets**, showing which opening bracket pairs with which closing one. Parentheses are, in his words, "very, very important in writing the rule". Draft there, then copy-paste into the editor. S13 44:30 · S13 53:00

`

### 15.13 · Old screen vs new — `Formula Condition`

**

This is the biggest structural point in the session. The doctor wants three separate conditions on TSH: **(1)** normal passes. **(2)** from 0.2 to 5 passes **for females**. **(3)** from 2 to 4 passes **for males**. S13 41:00

[FIGURE]  [IMAGE img/img-116.webp — الشروط الثلاثة في Notepad++]

_Caption:_ **`**The three conditions in their literal syntax** as the trainer wrote them: `norm{TSH}`, then `{TSH} > 0.2 and {TSH} <5 and sex=2`, then `{TSH} > 2 and {TSH} <4 and sex=1`. Note `sex=2` for female and `sex=1` for male. S13 45:40

[NOTE:BAD] On the old screen: three separate rules do not work 

The result is checked against **all three rules**. It passes the first and is then **stopped by the second**, because the second is not satisfied — and the result is reported as stopped. In the trainer's words: "it passes the first, the second stops it". S13 42:30

**`

**The old workaround** — everything in one rule joined with `Or`, so a failing first condition falls through to the second and then the third:

`
norm{TSH}  or  ({TSH} > 0.2 and {TSH} < 5 and sex=2)  or  ({TSH} > 2 and {TSH} < 4 and sex=1)
`

This is why old-screen rules balloon, and why the brackets matter so much.

[NOTE:TIP] `The new fix: `Formula Condition**`

A **`Formula Condition`** field was added beside the `Formula`. The engine evaluates **the condition first**: if it returns `true` it enters the rule; if it returns `false` **the rule is treated as if it does not exist** and does not stop the result. That is what lets the three cases live as three separate rules. S13 44:00

| # | `Formula Condition` | `Formula` |
| --- | --- | --- |
| 1 | — | `norm{TSH}` |
| 2 | `Require TSH and sex=2` | `{TSH} > 0.2 and {TSH} < 5` |
| 3 | `Require TSH and sex=1` | `{TSH} > 2 and {TSH} < 4` |

**

A male patient reaches rule 2, whose condition returns `false` — so it is skipped entirely — and falls through to rule 3, which applies. **Anyone taking Auto Verification new gets the new screen; anyone still on the old one must use the single-rule-with-`Or` style.**

`

### 15.14 · `LR`, `N.D.C` — and why `No LR` exists

_`

The doctor says: _"release a normal TSH, provided the previous result was normal too"_. You cannot write `Normal LR` — **two operators may not follow each other**. So you write the last result **numerically against the reference range**: S13 27:00

`
norm{TSH} and {LR} > 0.1 and {LR} < 5
`

[NOTE:BAD] `⭐ `N.D.C` stops every new patient `

Every rule using `LR` or `N.D.C` **silently assumes the patient has been registered before**. A new patient **has no last result** — the condition cannot hold — **so `N.D.C` stops him**. The same happens to a returning patient whose previous visit fell **outside** the configured window (ALT set to 30 days, he last came 40 days ago). S13 50:00

`**`The cure — `No LR`.** Add the button to the rule and `N.D.C` will not stop a patient with no last result (or none inside the window). The trainer states plainly this is **why the button was added to the screen**. The period is written the same way against the `Test Setup` value: delta set to 20 days and the patient returns on day 30 — the rule carries `> 20`, "according to the setup and what you entered".

### 15.15 · Worked rules from the field

[FIGURE]  [IMAGE img/img-117.webp — شيت قواعد حقيقي]

_Caption:_ **`**A rules sheet the trainer opens as a teaching aid.** Its columns are `TYPE` · `TEST_CODE` · `GROUP` · `UNIT_NAME` · `GROUP_NAME` · `TEST_NAME` · `MED_CODE` · `FORMULA` · `RULE` · `RULE_STATUS` · `RULE_DESC`. The description column carries the doctor's medical wording; the formula carries your technical translation. S13 52:16

**A · Phosphorus split by age (old-screen style, two rules)**

`
{Phosphorus} < 5  and  age > 365*18      ← adult
{Phosphorus} < 7  and  age < 365*18      ← child
`

**B · Bilirubin restricted to one device**

`
{Bilirubin Total} > 0  and  {Bilirubin Total} < 30  and  dev = 122
`

**C · A lab with two machines — the same trap, the same cure  The rule is written for device 144, and a result arrives from the other machine — **so it stops**. Add an `Or` arm: `… and dev = 144 or dev <> 144` — and the second machine's results fall through to its own rule, whose values the doctor gives you separately. S13 56:20
`
**D · An automatic comment (`Auto Comment`)** `  The conditions are joined with `And`; if **all** hold the comment drops onto the report, and if **one** fails there is no comment at all. Most of them live on the `CBC`. The example the trainer built live: when haemoglobin, MCV and MCH are all normal, the comment `Normocytic Normochromic RBCs` is attached automatically. S13 57:30

`
norm{HGB} and norm{MCV} and norm{MCH}
    with comment {HGB} like #Normocytic Normochromic RBCs#
`

### 15.16 · The editor, `Check Rule` and `Help`


1. **Double-click** a formula in the grid to open the **editor**. **
2. ⭐ Note: **the grid shows test names, the editor shows test codes**. It is one rule in two representations — do not think you are looking at something else. `
3. Type in the editor directly, or paste what you drafted in `Notepad++` and press `OK` — far faster than clicking `and`, `Prep`, `dev` one button at a time. **`**`**
4. `Check Rule` tells you the rule is **`valid`** or **`invalid`**. `invalid` usually means a **bracket problem**; otherwise review the rule. 
5. `F8` re-reads all the rules, and the `Active` column decides which rule is in force.

[NOTE:TIP] `The `Help` column — a recommendation that reaches the doctor _

Text shown to the doctor alongside the result. The trainer's example: if haemoglobin is low and ferritin is low, the text reads _"Recommended to do TIBC"_. S13 01:00:30

[NOTE:INFO] There is no single correct rule

The trainer closes on this, verbatim: "a rule has no standard you can write it by. Writing the same rule can differ from one person to another, depending on the ordering and on how many conditions you have. But in the end it must come back with the correct verdict and actually satisfy what the doctor wants". S13 01:01:40

---

<!-- section c16 -->

## 16Worked scenarios — with the real numbersComplete

Seven cases as they really happen at reception, each from start to finish, with the numbers that appeared in training. Read them as if you were doing them.

#### 1 · New cash patient, four tests, a discount and payment

Mr TEST, born 24-11-1995, phone 20000, wants liver, lipids, uric acid and a blood count. No contract.

`
1. `Registration`. `Patient No.` empty. `Title` = `الأستاذ` → `Sex` = `Male`. Name TEST + `Enter` — no match. 
2. `Birth Date` 24-11-1995 + `Enter` → 11 D 3 M 26 Y. `Tel. No.` 20000. `Rank`/`Relative` stay `Normal` — cash. 
3. `Patient Tests`. In `Shortcut`: `F9` "AST" `Enter` → AST (SGOT) 15. "CBC" → 35. "LIP" → Lipids Profile formula 105. "URIC" → Serum Uric Acid 15. `Total Required` 170, `T. Count` 4. `
4. A 70 discount: type it into `Discount` + `Enter` → `Disc. Per.` 41.18%. `
5. `F10`. `Acc. No.` 1784 and `Patient No.` 292010 appear. Two tubes print: 22030000005 (Serum — the three chemistry tests) and 22030030002 (EDTA — CBC), plus a job order. `
6. `Payment` appears: `Total Required` 170, `Discount` 70.01, `Remaining` 99.99. The patient pays 99.99 → into `Paid` + `F10` → receipt, `Remaining` 0.

**Outcome**`The patient leaves with a receipt; two labelled tubes and the job order go to the draw. The job order says `This is the visit No. 2` — so this patient had an earlier visit under the same number.

#### 2 · A returning patient without a number — and a common name

"Ahmed Mohamed" returns after two months, doesn't recall his number, and the lab has dozens of that name.

`
1. `Registration`, type the name + `Enter` → `similar name exist`.
2. Open the list: several "Ahmed Mohamed", each with a patient number and phone. Ask for his phone — it matches one row. `
3. Select the row. His data drops in: `Rank` syndicate, address, an old comment "on aspirin". Update the phone if it changed. **
4. `Patient Tests` → tests → `F10`. A new accession under the **same** patient number.

**If no warning appeared**`(the option isn't enabled at your site) search before typing: `F7`, his phone in `Tel. No.`, `F8`. Never create a second file.

#### 3 · A non-fasting patient wants lipids — deferral

Came for a blood count and lipids, but ate breakfast. Lipids require fasting.


1. Register normally and add both tests in `Patient Tests`. The `Lipids Profile` row is highlighted — open it: "12-hour fast". `
2. On the lipids row: `Status` → `Reserved`. `F10`. Only the EDTA label prints; no Serum tube today. The patient pays for both or for the count only — per your lab's policy. 
3. Tomorrow, fasting: `F7` his accession `F8` → `Patient Tests` → `Status` to `Ordered` → `F10`. The Serum label prints and the draw happens.

**The point**One visit, one bill, no test lost and none charged twice.

#### 4 · A home visit — from booking to registration

"adham test", 30, calls asking for a home draw tomorrow.


1. `Home Reservation`: `Title` Mr, name, age 30 in `Y`, `Tel. No.` 0123456789, full `Address` (mandatory), tests in `Test Names`. `F10` → `Booking No.` 140. Print for the phlebotomist. `
2. Next day the phlebotomist returns with the sample. `Registration`: `Booking No.` 140 + `Enter` → everything drops in. `
3. `Patient Tests` — the booked tests are there — `Done By` = the phlebotomist (for their share) → `F10`.

**Outcome**An ordinary visit with its labels; the patient's name was typed only once, on the day of the call.

#### 5 · A company sends ten employees for the same check

A contracted company, periodic check: blood count and glucose for all ten.

`
1. `Batch Registration`. At the top: `Rank` = the company's contract, `Relative` = the employees' tier. `
2. Ten rows: title, name, age in `Y`. `Patients Count` = 10. (For anyone with a file, type their number in `Patient No.` — no similarity warning here.) 
3. `Patient Tests` once: CBC and glucose → `F10`. Ten job orders and twenty labels print together, under one batch number.

**The bill**On contract — no payment screen. Were the company cash: `Batch Payment`, the batch number, `Pay All`.

#### 6 · A small lab sends ready tubes

Lab1's courier arrives with five tubes drawn at their site, each with a slip naming the patient and tests.


1. `Lab. Registration`. `Lab` = Lab1 (`F9`) → courier Ahmed Mohamed, the lab's contract and phone drop in; `Batch No.` 20. `
2. Per slip: title, name + `Enter`, age, sex; then `ShortcutF9` for the tests — they land in `Services`. Untick `Sample Collected Inside the Lab` since the draw happened at their site. `
3. `F10`. Labels for the five tubes go over the sending lab's slips.

**The bill**`On the sending lab's contract — settled periodically via `Batch Payment` by batch number, and the courier's reports build on what you registered.

#### 7 · A partial card payment, then cash to finish

99.99 due. The patient pays 50 by card now and the rest on collecting the result.

`
1. In `Payment` (after the registration's `F10`): `Paid` 50, tick `Visa`, `Transaction No.` the card slip number, `F10`. A receipt prints, `Remaining` 49.99. `
2. Two days later he's back: `RegistrationF7` the accession `F8` → `Patient Tests` → `Money`. The card row is there; new row: `Paid` 49.99 → `F10`.

**Outcome**`Remaining` 0, two receipts, and `Print Invoice` gives him one invoice for the total if asked.

---

<!-- section c17 -->

## 17Questions staff askComplete

**Where is the Save button?**

`

There isn't one. Saving is `F10` on every screen. The disk icon on the toolbar does the same.

**The system refuses to save — why?**



Usually an empty red field (`Patient Name`, `Birth Date`, `Sex`) or a blue one your lab made mandatory (phone, doctor). Fill it and press `F10` again.

**Do I type the patient number or leave it?**

Leave it. The system generates it. Typing it makes duplicate files, and the number never changes once issued.

**The patient came without their number — what now?**

_

Type the name + `Enter` and wait for the similarity warning, or `F7` the phone in `Tel. No.F8`. Compare name _and_ phone.

**Why one tube for three tests, and two tubes for two?**

The system merges one unit's tests on the same sample type into one tube, and splits anything whose unit or sample type differs. You don't decide that — test setup does.

**I can't find the test in any group**



Don't browse the groups. In `Shortcut` press `F9` and type three letters of its name.

**A test shows in a different colour in the list**

It has precautions (fasting, say). Open it, read them and ask the patient now — before the draw.

**The patient isn't fasting — delete the test?**

`

No. `Status` → `Reserved`. It stays in the visit with no sample, and you set it back to `Ordered` when they return fasting.

**The Payment screen didn't appear after saving**



The patient is on contract (`Rank` other than `Normal`) so nothing is due from them — or you lack the right to see it. For cash it also opens from `Patient Tests` → `Money`.

**I typed a 70 discount and it shows 70.01**

Percentage rounding (41.18%). Normal and harmless.

**I need to reprint one damaged label**

Registration` → `Sampling Details` → the `B` button on that sample's row.

**The patient paid part — how do I take the rest?**

`

Retrieve the visit (`F7` its number `F8`) → `Patient Tests` → `Money` → a new row with the amount → `F10`.

**I closed the screen by mistake and got "Do you want to save"**

Yes` saves and closes, `No` discards what you typed. If unsure, press `Yes`.

**A pregnant patient's normal value flagged "out of range"**

Patient Condition` wasn't set at registration, so it was compared to the general range. The field is filled at registration.

---

<!-- section c18 -->

## 18TroubleshootingComplete

| What you see | Likely cause | What to do |
| --- | --- | --- |
| Registration won't save | A mandatory field (red, or blue set mandatory) is empty | `Fill it, then `F10` |
| A result can't be entered for a test | The sample isn't `Received`, or the test is `Reserved` | `Scan the sample at the unit; or set the test back to `Ordered` and draw it |
| A profile won't advance though its tests look done | One of its tests is lagging | `Open the profile with `Tests` and find the straggler |
| The label prints a number with no bars | `No printer named "barcode", or the `Code 128` font isn't installed | Engineer: define the printer, install the font, Reports 6.0 |
| A patient's attachment is missing at another branch | Attachments don't transfer between branches by default | Open it at the originating branch; or ask the developer to enable transfer |
| Payment screen doesn't show | Contract patient, or missing right | Check `Rank`; open it from `Money` |
| Two files for one patient | New Patient` chosen wrongly, or the number typed manually | No merge available to the clerk — tell the admin; in future compare name and phone |
| Can't find a test in its group | The classification isn't what you expected | `Shortcut` + `F9` + three letters |
| An existing name became a new patient in a batch | `No similarity warning in `Batch Registration` | `Enter `Patient No.` manually for those with files |
| `Preview` refuses: `There are other results have no value` | Tests in the visit still have no result | `Complete them or set them `Reserved`, then preview again |
| A result won't print from the print list | `It isn't `Verified` | Check its row colour; send it for sign-off |
| `A sample doesn't show in `Search` | `☑ `Received` is ticked and it isn't received, or it's outside the date range | `Untick it and widen `Date From` |
| A case in `Search` but not in `Worklist` | The first filters by registration date, the second by collection date | `Draw the sample or widen the `Worklist` range |
| Delta Check` ticked on a row | The result moved from the previous one by more than the configured band | Procedure 8.7: instrument, then patient, then sample |
| A whole PCR run never reached the system | The service never read the file, or read it and rejected it | `Follow the file: `ResultFolder` ⇒ `BackupFolder` (read) or `FailedFolder` (rejected) — chapter 14 |
| A PCR file is rejected every time although it is from the right machine | A gene name, the column order or the well sequence changed in the machine’s software | `Compare the rejected file with the original `run file` the service was built on |
| A PCR result appeared but looks wrong | The interpretation is configured with thresholds that are not the kit’s | `**Check the log’s `Flag` against the interpretation sheet — 14.10; and remember a **low** value is positive |

---

<!-- section c19 -->

## 19Still undocumentedReserved

What is still undocumented. No step is written before its source exists. (Test setup, analyser interfacing and PCR are now chapters 11, 13 and 14, and Auto Verification is now chapter 15.)

| Topic | Session | Length |
| --- | --- | --- |
| Installation, database, branches — excluded from this guide’s scope** | 1–4 | 4:39:28 |
| `Tools` · `Sample Storage` · `Quality Control` · `Sms` · `Help` | No source material at all |
| Running the Auto Verification engine, troubleshooting it, and investigating an existing rule | Promised by the trainer at the end of session 13; not yet supplied |

---

<!-- section c20 -->

## 20Glossary

| On screen | Term | Definition |
| --- | --- | --- |
| `Patient No.` | Patient number | The permanent file number; auto-generated, never changes |
| `Acc. No.` | Accession number | A sequential number per visit / test order |
| `Sample ID` | Sample ID | A unique number per tube, printed as a barcode |
| `Booking No.` | Booking number | Produced by `Home Reservation`; typed into `Registration` to pull the booking |
| `Batch No.` | Batch number | `Groups a batch's patients or a sending lab's samples; the key to `Batch Payment` |
| `Rank` / `Relative` | Contract / tier | Set the price list and the patient/payer split |
| `Patient` / `Insurance` | Patient / payer share | The two price columns per selected test |
| `Patient Condition` | Patient condition | Selects the results' reference range (pregnant, diabetic, smoker) |
| `Reference Range` | Reference range | The "normal" limits a result is compared to |
| `Groups` | Groups / units | The lab units tests are classed under, and that split tubes |
| `Panels` / `Profiles` | Packages / profiles | A test bundle priced as one unit whose status advances only when all its tests do |
| `Shortcut` | Shortcut | `The test-picking field: `F9` + part of a name, or a shortcut number |
| `Ordered` · `Collected` · `Received` | Sample statuses | Ordered · drawn · arrived at the unit (the result-entry gate) |
| `Complete` · `Review` · `Verification` | Test statuses | Entered · reviewed · signed off (e-signature) |
| `Reserved` / `Rejected` | Deferred / rejected | A deliberate clerk's hold / an unfit sample |
| `Job Order` / `Sampling Details` | Job order | A printout travelling with the tubes: tests, units, samples and barcodes |
| `Done By` | Drawn by | The user who drew the sample; for home-visit shares |
| `Representative` | Courier | Who collects samples from contracted labs |
| `System Parameter` | System parameters | A setup screen governing blue fields' requirement and automatic printing |

---

<!-- section c21 -->

## 21Needs confirmation

**

Everything not seen on a screen or stated by the source. **73 open items of 77.** "Blocking" prevents a whole chapter; "material" changes a step; "detail" sharpens accuracy.

| # | Item | Impact |
| --- | --- | --- |
| 1 | **The role list, their rights, and the user-management screen — **resolved in v1.6** from session 10 (chapter 12) | — |
| 2 | **Rank`/`Relative` and price-list setup (session 8) — **resolved in v1.9** from session 8 (chapter 10) | — |
| 3 | **The other eight sessions in full — **resolved in v1.9**: nine of twelve sessions are documented, the four installation sessions are excluded by the project owner, and what remains is listed in chapter 19 | — |
| 4 | The fields of the `System Parameter` screen and what it governs — only the path is known: `Setup ▸ Adminstiration ▸ System Parameters` (chapter 12) | Material |
| 5 | `Where `Auto Collect`/`Auto Receive` is configured — the name itself appears in the `S. T.` dialog (chapter 9) as `Auto Collect Location` and `Auto Recieve Location` | Material |
| 6 | `The `Conditions` screen: picker or setup? where ranges are tied | Material |
| 7 | The `Diagnosis` screen's role in registration; the `IDC Code` standard | Material |
| 8 | Where the similar-name option lives in setup | Material |
| 9 | The time limit on the cancel-test right and the refusal message — the rights `Delete Test Any Day` and `Delete and Active verified test` are listed in chapter 12 | Material |
| 10 | Editing patient data after save; cancelling a whole visit | Material |
| 11 | Literal steps in `Batch Payment`; how `Pay All` splits | Material |
| 12 | Cancelled` behaviour in booking; meaning of `Visit Date` and `Last Visit Date`; the booking's preliminary labels (not printed in training) | Material |
| 13 | `The contracted-lab setup screen | Material |
| 14 | `Why `Patient No.` stays empty in `Payment` for `Lab To Lab` patients | Material |
| 15 | `The `Tests Methods` button; the `Additional Services` and `Panels` groups | Detail |
| 16 | The `Recovery` field, the `E` button, refunds | Detail |
| 17 | "Result receipt" for contract patients and "Request Label" — names from speech | Detail |
| 18 | Sample ID` length (8/10/11) and the official unit-code list | Detail |
| 19 | Cross-branch attachment transfer — how it is enabled | Detail |
| 20 | Engineer printing requirements: Reports 6.0, Code 128 font, "barcode" printer | Detail |
| 21 | The `Area` field in `Lab To Lab` | Detail |
| 22 | Numbering rules for `Booking No.` and `Batch No.` | Detail |
| 23 | **The name of the result-**entry** right specifically — review, verify, edit and print are documented in chapter 12 (`R`/`V`/`E`/`P` per medical group) | Detail |
| 24 | `Whether a test becomes `Complete` on saving the result with F10 or via a separate button | Material |
| 25 | `Whether the `Preview_Without_Verify` right (chapter 12) covers printing or only preview, and the print list's official print button | Material |
| 26 | Whether `Review`/`Verify` on the Search screen act on the whole list or the selected rows | Material |
| 27 | The `Search By Test Result` and `Search By Birth Date` screens | Material |
| 28 | What the `R` button in the Results header does, and the `Image` button | Detail |
| 29 | `The `P` and `C` grid columns, and which button opens the `Test / Analyzer` dialog | Detail |
| 30 | `The `R. Date` field on the Search screen | Detail |
| 31 | `What `T`/`M` in the `Type` column expand to, and the `Internal Code` rule (`C22/2846`) | Detail |
| 32 | The blue field colour: "mandatory + list" (session-6 trainer) or "configurable mandatory" (session-5 trainer) | Detail |
| 33 | The default `Delta Check` percentage and period — the setup dialog itself is documented (`Panic and reportable range`, chapter 11) | Detail |
| 34 | `The rejection-reasons setup screen name ("Sample Reject Reason" from audio), the `Unreject Sample` message wording, and where the reason shows later (session 7) | Material |
| 35 | Steps to issue a new sample with a new barcode after rejection (the ISO road) | Material |
| 36 | `The three delay colours on `Worklist`, their default thresholds and setup screen | Detail |
| 37 | The meaning of the `Discounts`, `Grouping` and `Auto` options on `Worklist` | Detail |
| 38 | `**Rights for the financial screens (price lists, `Return Money`, `Money Transfer`, `Delete Patient`) — **merged into item 46** in v1.9 | — |
| 39 | `The effect of `Delete Patient` on samples and results, and verification of its steps — the steps in chapter 10 were reconstructed from frames alone (audio missing) | Material |
| 40 | `Whether the `Second`/`Third` lists in `Client Price List` are fallbacks for tests missing from the first; and how `Restore from main price list` treats `Exclude` | Material |
| 41 | Which `Income` button yields the combined cash-desk daily and which the compact `Total Daily Income` | Material |
| 42 | Type` values for a Relative other than `Normal`; the meaning of `Money Format`; an "All" `Branch` value in `Invoice` | Detail |
| 43 | `The `Canceled Patients Or Tests` · `Pateint Transfer` · `Additional Services Report` · `Representative Income` reports · the `Query` printout | Detail |
| 44 | The `Share` field (doctor percentage) in `Setup ▸ General Data ▸ Doctors`; 9.3.0 vs 9.2.0 differences on the financial screens | Detail |
| 45 | `The whole `Outsourcing` module (setup and reports) | Blocking |
| 46 | `The literal `Object name` entries for the `Setup ▸ Tests Setup` and financial screens in `Groups And Privileges` — the mechanism itself is documented in chapter 12 (`Q`/`I`/`U`/`D` per screen) | Material |
| 47 | `The practical difference between `Test Sum` and `Profile Sum` on the `Profiles` screen | Material |
| 48 | The `Case Sums Chapter` and `Small` fields — mentioned in speech, never seen on screen | Detail |
| 49 | `The literal name of the text-report template (`new_text`?) | Detail |
| 50 | `Whether `Frequency Check` catches orders coming from different branches or places | Material |
| 51 | `What is inside `Setup ▸ Tests Setup ▸ Fonts ▸` (not the `Fonts` button on the `Tests` screen) | Detail |
| 52 | Last Result` versus `Urgent` on the `Tests` screen | Detail |
| 53 | `Six `Setup ▸ Culture` screens never opened: `Antibiotics` · `Culture Antibiotics` · `Culture Organisms` · `Organisms` · `Organisms Antibiotics` · `Sensitivity` | Blocking |
| 54 | `The `Formula Setup` dialog and the literal formula syntax | Material |
| 55 | `Conf` on `Analyzers ▸ General` — the trainer said outright he does not know what it means | Detail |
| 56 | `Is `Form Name` still required with a Windows Service, or only for the old forms? The recording contradicts itself | Material |
| 57 | The shape of the inner `Details` dialog in `Analyzers Tests` — no frame captured it | Detail |
| 58 | `Intercept` and `Cut Off` — their explanation was deferred | Detail |
| 59 | Where `R.S` is really set — the `Analyzers Tests` screen or the service? | Material |
| 60 | Can `Auto Print` release a result without the auto-verification engine? — the engine and the `Medical check` checker are documented in chapter 15 | Material |
| 61 | `Period` in `System Users` — the exact meaning; most likely a password-change interval in days | Detail |
| 62 | `The `Automatic Signature` image size — a "suitable size" was mentioned with no number | Detail |
| 63 | Analyzer Code2` in `Analyzer Culture Tests` — the difference from `Analyzer Code` was never explained | Detail |
| 64 | The SOLGENT interpretation sheet gives `ORF1a +` · `RdRp −` · `N −` · `IC +` two different verdicts in two rows — which one governs? | Blocking |
| 65 | A lab running two kits on one PCR machine whose software exports to a single path — how is it actually set up? | Material |
| 66 | `Does `Parameter` on a QuantStudio service hold the `Instrument Serial Number`, as it holds the `Optical Head Serial Number` on a BioRad? | Material |
| 67 | What do `AnalyzerVersion` and `CommonVersion` select in the service config? (both 1 in the example) | Detail |
| 68 | `Is the `_1` in `Biorad_Solgent_1` an instance counter (a second machine being `_2`) or part of the name? | Detail |
| 69 | `How many read retries, and over what interval, before a file is moved to the `FailedFolder` | Detail |
| 70 | The `LMP Preparation No.` field on `Verify Setting ▸ General Settings` — on screen, never explained | Detail |
| 71 | `Four checkers visible on `Units Settings`, none of them explained: `Missing Result check` · `Quality Control check` · `Online Verification` · `Reportable check` | Material |
| 72 | `**`A contradiction: `Main Rules check` is **ticked** in the demo while the trainer said it is "a modification we do not use and will not explain" — and its relation to the `Main Rule` grid column is unresolved | Material |
| 73 | Buttons on the `Medical Rules` keypad that were never explained: `Abs` · `Lowd` · `LMP` · `Then` · `desc1` · `desc2` · `desc3` · `loc` · `^` | Detail |
| 74 | `The device codes in the rule examples (122 · 140 · 144) do not match the training VM’s `Analyzers` list (20–31) — the examples come from a customer sheet | Detail |
| 75 | **Is the new `Medical Rules` screen with `Formula Condition` present in LDM 9.2.0 at all? Every frame in the session shows the **old** screen; the new one was built at a customer site | Material |
| 76 | `A 40-second transcription gap at `00:21:40–00:22:20` of session 13 — bounded on both sides, and the screen state through it is known from the frames | Detail |
| 77 | Five `Setup ▸ Adminstiration` screens never opened: `Menu Management` · `Rights` · `Rights Adminstiartion` · `System Objects` · `Monitor Sessions` (S13) | Material |

### Change log

| Version | Date | What changed |
| --- | --- | --- |
| 0.1–0.2 | 2026-09-07 | Preamble and concepts from the concepts document and barcode image; then the statuses chapter and the start of registration from session 5 |
| 1.0 | 2026-09-08 | Full rebuild after transcribing all of session 5: 7 procedural chapters, 13 procedures, 7 numbered scenarios, 14 screenshots, keyboard keys, the Payment, Samples, Batch, Lab To Lab and Home Reservation screens |
| 1.1 | 2026-09-09 | The Results and Search chapter (08) from the full session-6 transcript: 13 procedures, 4 scenarios, 13 questions, 12 new screenshots; chapters renumbered 09–14; 12 new needs-confirmation items and five troubleshooting rows |
| 1.2 | 2026-09-09 | The Worklist and sample-rejection chapter (09) from session 7 (up to 11:51; the rest excluded): 5 procedures, 2 scenarios, 7 questions, 5 screenshots; two new concept sections (the two search modes and %, the record unit on the four screens) from National Technology material; chapters renumbered 10–15 |
| 1.3 | 2026-09-10 | The Financial chapter (10) from the full session-8 transcript (overnight run): 14 procedures, 4 scenarios, 12 questions, 25 screenshots; chapters renumbered 11–16; 8 new needs-confirmation items |
| 1.4 | 2026-09-10 | The test-setup chapter (11) from the full session 9 transcript. |
| 1.5 | 2026-09-10 | A pass over terminology and cross-chapter references. |
| 1.6 | 2026-09-13 | The users & privileges (12) and analyser-integration (13) chapters from session 10 — which cleared the permissions-matrix blocker. |
| 1.7 | 2026-09-13 | The PCR-integration chapter (14) from the two PCR recordings: interpretation thresholds, one service per machine and kit, the three folders, and reading the log. Two claims in chapter 13 were corrected from the source: `Passed Check`, not `past checked`, and the log is hourly, not daily. The needs-confirmation table was brought up to 69 items. |
| 1.8 | 2026-09-13 | The Auto Verification chapter (15) from session 13: the setup screen order, the seventeen checkers, the complete `Medical Rules` operator dictionary, the `H-Val`/`L-Val` formula as a multiplication rather than a subtraction, and `Formula Condition` as the replacement for the single giant rule. Chapters 15–20 were renumbered to 16–21, and the needs-confirmation table was brought up to 76 items. |
| 1.9 | 2026-09-13 | `A pass over the needs-confirmation table against the body text: items 2 and 3 were resolved (Financial has been documented since 1.3; nine of twelve sessions are covered), 38 was merged into 46, nine partly-answered items were narrowed against chapters 11 and 12, two duplicates were removed, and the five unopened `Setup ▸ Adminstiration` screens were added. The table now stands at 73 open items of 77. |

LDM 9.2.0 System Guide — Engineer Master Edition · v1.9 · 2026-09-13 National Technology · `Knowledge_Base.md`

× [IMAGE external]
