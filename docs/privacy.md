# Privacy and law

**Dated November 2026. This is not legal advice and it has not been reviewed by a lawyer.** It records what project research
found in public sources, for the design of the library and for you to check. Laws, drafts and DOPA practice change, and the
research found little from the most recent months. Ask a lawyer before you rely on any of it.

## Who is responsible

This library is a tool in between. By the decision of the project owner, **the people who use it, on the side that reads a card
and on the side that receives the data, are responsible for the holder's consent and for the law that applies.** They choose the
mode for each read. That position is a project decision and not a legal conclusion, and it has not been reviewed by a lawyer.
Whether the library's author has any role under the PDPA, and whether publishing a list of APDU commands raises an issue, are
open questions.

## What each mode reads

### `Public` (default)

From the MOI applet: `CitizenId`, Thai and English names, `BirthDate`, `Sex`, the address, `IssueDate`, `ExpiryDate`, `Issuer`, and
the photo only with `IncludePhoto = true`.

### `PublicAndPrivate`

Everything in `Public`, and these. Names stay by position wherever public sources disagree on the meaning, so nothing here claims
more than the evidence does.

| Property | Source | What public sources say |
|---|---|---|
| `Private.LaserId`, `AdmAppletDataRaw` | ADM applet, `80 00 00 00 07` and its GET RESPONSE | Public code reads a Laser ID here. The 7 byte header and 16 byte slot are an inference that fits several projects. No trace has been published |
| `Private.Nhso.Raw0004` to `Raw015D` | NHSO applet, 9 ranges | Public code labels these as main and sub insurance, main and sub hospital, paid type, issue, expire, update, and change of hospital count. No source says whether they keep up with current entitlement, or whether every card has this applet |
| `Private.CardVersionRaw` | MOI `0000-0003` | Labelled card version. Values across generations are not known |
| `Private.MoiRaw00E2` | MOI `00E2-00F5` | Labels conflict, among them request number and registration code |
| `Private.MoiRaw0177` | MOI `0177-0178` | Labels conflict: **religion** in some projects, card type in another, version in another. Not settled. It is not called `Religion` |
| `Private.MoiRaw1619` | MOI `1619-1626` | Labels conflict, among them card number and reference number |

`Private.ChipDataRaw` exists in the API and **is always `NotRead`**. The only public source for chip data, `80 CA 9F 7F`, starts with
a SELECT that names no applet, and what that leaves selected cannot be bounded without a real card. The guard refuses both
commands in every mode.

A failure in private mode costs only that field. Nothing in private mode has been checked on a real card.

## What is never read, in any mode

A guard under the read logic refuses these, and the tests hold it to that.

| What | Why |
|---|---|
| PIN and VERIFY: `80 20`, `80 B4` | Cards keep a retry counter, so this is a risk to the card, whatever the consent |
| Fingerprint match: `B0 34`, and the Bio applet | A DOPA training document says the chip holds index fingerprints. This library has no use for them |
| Write and life cycle commands | The library only reads |
| `1627-173D` | One public project marks this "Unknown (Fingerprints?)" |
| `015A-0166` | Labels conflict. Its length matches a citizen ID, so it may be data about an officer, a third party. That is an inference |
| SELECT with no AID, `80 CA` | See chip data above |
| Anything not on the allowlist | For example `0179-017A`, which conflicting sources call a length or unknown |

## The law, as far as the research reads it

**Identity Card Act.** As the text reads, section 7/1 provides for the card memory, and bars disclosing that data to anyone who did not
make the card, except the general data on the card or where necessary under set conditions. Section 12 sets a penalty for accessing or disclosing data in that memory, other than the general
data shown on the card under section 7, without the holder's consent: imprisonment up to 5 years, a fine up to 100,000 baht, or
both. The wording the research had came from a PDF text layer with a watermark, so compare it with the original:
[Act text](https://report.dopa.go.th/laws/document/1/22.pdf),
[DOPA penalty table](https://www.bora.dopa.go.th/wp-content/uploads/2022/03/rate.pdf).

Where `Public` mode stands against that line is **an inference, not a finding**: the listed items probably fall under the general
data exception, but sex and the expiry date are not in the minimum list of section 7, so a lawyer should look at them. The items
of private mode are outside that line by the same reading, which is why they need an explicit choice.

Section 15 bis, on unlawfully holding another person's card, is a matter for kiosks that keep a card
([Act text](https://report.dopa.go.th/laws/document/1/22.pdf)).

**PDPA.** Section 26 names religion and health data among sensitive data
([section 26](https://pdpathailand.com/pdpa/content/article26.php)). Since `MoiRaw0177` may be religion and the NHSO ranges concern
health coverage, treat both as sensitive until shown otherwise. The status of the chip photo under section 26 is open.

**Permission from DOPA.** Whether an application reading through PC/SC needs permission is the most important open question, and
this library does not claim that it does or that it does not. A secondary source reports a DOPA notice of 8 October 2018 that lets
private parties with a need read with consent, after accepting conditions in writing
([Line Today](https://today.line.me/th/v3/article/kE8NOno)). The research could not find the notice itself. In March 2026 DOPA
withdrew a political party's use of DOPA-Digital ID and of a card reading program, after reports of a leak
([Thai PBS](https://www.thaipbs.or.th/news/content/503362)). **Commercial users should check with DOPA.**

**Still changing.** A draft ministerial regulation of June 2026 would change fingerprint collection and define two card forms
([Thai Post](https://www.thaipost.net/general-news/1012060/)), and an amendment to the PDPA is also under discussion
([Chambers 2026](https://practiceguides.chambers.com/practice-guides/data-protection-privacy-2026/thailand/trends-and-developments)).
The research found no outcome of the draft. Check [law.go.th](https://www.law.go.th/) and
[bora.dopa.go.th](https://www.bora.dopa.go.th/).

## Reading a card is not proof of identity

The result is what a card returned. `90 00` and a valid check digit show that a card answered, and that a number has the right
form. They do not show the card is genuine, valid today, or held by the person in front of you.
[ETDA guidance](https://www.etda.or.th/getattachment/newsevents/pr-news/news-standard/23022566/Digital-ID-Infographic_V3-IAL.pdf.aspx?lang=th-TH)
treats checking the data, checking the card's status and binding it to a person as separate steps. Card status is an online service
for authorized agencies ([Thansettakij](https://www.thansettakij.com/politics/651988)). The research found no public evidence that an
independent library can verify chip data as genuine, and no report of signatures or card authentication on current cards either.
That means unknown, not absent. So there is no `IsVerified` or `IsGenuine` here. `PersonCategoryCode` is the raw first digit: do not
read nationality, age or current address from it.

## Laser ID

Views conflict. DOPA has called it a card control number and not personal data
([Thai PBS](https://www.thaipbs.or.th/news/content/502377)). A 2026 preprint reports that a citizen ID together with a Laser ID could
get past GDX authentication ([arXiv 2604.23538](https://arxiv.org/html/2604.23538)). Whatever its legal status,
treat it as a secret: this library reads it only in private mode and never puts it in a log or a trace. Do not log or store it.

## What the library does and does not do for you

Tests hold it to these:

- `ToString()` of the data types, exception messages, diagnostics and the output of `thaiid-diagnose` hold no card data
- `TraceEntry` holds only enums and numbers
- the library has no logging at all
- `Public` mode does not touch private ranges, and no mode touches the forbidden ones
- the samples hold no real data, store nothing, and the console sample masks the citizen ID by default

It cannot do these:

- Clearing buffers is best effort. Managed memory gives no guarantee, and strings cannot be cleared
- Once your code has a value, what happens to it is yours to decide
- Reader names can hold a device serial. Serials are removed from diagnostics on a best effort basis, since real names have not been
  seen here

## If you build on this

This is a starting point and not a compliance list.

- Read only what your purpose needs: `Public` without the photo if that is enough. Choose `PublicAndPrivate` per read and per holder,
  never as a default.
- Get and record consent before a read, in the way your lawyer advises. How to record consent under section 12 is an open question.
- Do not write card data to logs, temp files or analytics, and do not send it anywhere the holder has not agreed to.
- Do not treat a read as identity verification.
- If you collect data, you may have PDPA duties of your own. A person's card image in a public issue is personal data too, so never
  paste real card values or photos into a bug report. `thaiid-diagnose` exists so that you need not.
