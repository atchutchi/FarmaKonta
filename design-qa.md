# NôFarma inventory design QA

final result: blocked

## Scope

Comparison of the approved inventory board in `docs/design/previews/03-products-stock-purchases-suppliers.png` with the native Windows implementation of Products, Stock, Purchases, Suppliers and Initial Inventory Import.

## Evidence inspected on 2026-07-28

The native application was inspected live with Windows Graphics Capture at 1011 by 634, 1536 by 816 and the approved physical window size of 1366 by 768. At 125 percent Windows display scaling, the 1366 by 768 window is represented as approximately 1080 by 608 logical pixels. The screenshots were displayed during the active QA session but were not persisted in the repository. The following states were visually inspected during the same run:

1. Products empty state and inline new-product form.
2. Suppliers empty state.
3. Stock empty state.
4. Purchases empty state and disabled receipt form.
5. Import file selection, automatic column mapping and validation result.

The application was rebuilt after the responsive-header fix. An administrator completed the login manually. The corrected shell and all five inventory pages were then reviewed at the approved physical window size without automating credentials.

## Comparison result

### Products

Health: good at the inspected sizes, including the approved 1366 by 768 physical window.

The title, primary action, persistent search, honest empty state and inline editor follow the approved structure. The form remains scrollable at the smaller size. The implementation does not insert sample medicines or fixed totals from the reference board.

### Suppliers

Health: good in the empty state.

The primary action, search area and educational empty state are clear. The detailed supplier debt and purchase panels cannot be compared until real supplier data exists.

### Stock

Health: good in the empty state.

The page keeps the approved title, search and table-oriented structure. Real alert counts appear in the shell. Lot, expiry and FEFO rows cannot be compared until test data is created.

### Purchases

Health: good at 1536 by 816 and usable with scrolling at 1011 by 634 and at 1366 by 768 with 125 percent Windows scaling.

At the larger size all table headers and receipt fields are visible. At the smaller logical sizes the two-column layout compresses the table and requires scrolling in the receipt pane. The vertical scroll container exposes every field and the disabled confirmation state is clear. A future breakpoint should stack the receipt pane below the purchase list for windows materially narrower than the supported 1366-pixel target.

### Initial inventory import

Health: good through validation.

The four-step indicator, local-file reassurance, automatic required-column mapping and persistent validation flow are clear. The official empty CSV model is parsed through the production reader and an automated regression test confirms that it contains the required headers and no data rows.

## Accessibility observations

- Navigation, form fields and buttons were exposed through Windows UI Automation with useful names.
- The receipt action remained disabled until required values are available.
- Controls use visible labels and large targets.
- Keyboard navigation was exercised from the supplier page into the shell navigation and the focus indicator remained visible.
- Full screen-reader announcements, contrast ratios and 200 percent zoom were not measured in this run.

## Corrections made during QA

- Added a compact shell state below 1450 pixels so secondary status labels do not collide with the sign-out button.
- Reserved a dedicated header column for sign-out, limited dynamic name widths and added ellipsis trimming.
- Moved the complete operational header to a safer 1450-pixel breakpoint and hid secondary status labels in compact mode.
- Removed the code-level visibility override that defeated the responsive states.
- Added a regression test proving that the official CSV model has the required headers and no data rows.
- Set the initial desktop window to the approved physical size of 1366 by 768 and protected it with a regression test.

## Blocking conditions

The empty and initial states have been compared directly with the approved board and the responsive defect found during the comparison has been corrected. The strict final result remains blocked only because the approved board shows populated products, lots, purchases and suppliers while this local installation intentionally contains no invented business data. A same-state pixel-level comparison requires representative records supplied or explicitly approved as disposable QA data.
