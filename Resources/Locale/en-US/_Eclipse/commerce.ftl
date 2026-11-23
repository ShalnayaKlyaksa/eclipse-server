commerce-title = Payment terminal
commerce-refresh = Refresh invoice
commerce-claim = Register to my ID
commerce-management = Register management
commerce-help = Load a held item, then issue an invoice. Payment releases the item to the buyer. Without an item, the invoice pays for a service. Commission is included in the price. Management requires the owner's ID card.
commerce-description = Payment description
commerce-amount = Total price, cr.
commerce-recipient = Revenue destination
commerce-personal-id = Recipient ID
commerce-personal = Personal account
commerce-department = Department budget
commerce-commission = Commission, % (0–100)
commerce-fee-department = Commission recipient
commerce-issue = Issue new invoice
commerce-load = Load held item
commerce-return = Retrieve item
commerce-invoice = Owner: { $owner }
    Description: { $description }
    Item / service: { $item }
    Recipient: { $receiver }
    Total: { $price } credits
    Recipient: { $net } cr. • Commission: { $fee } cr. ({ $percent }%) → { $department }
commerce-service = Service without automatic delivery
commerce-pay = Confirm payment — { $price } cr.
commerce-no-invoice = No outstanding invoice
commerce-success = Operation completed.
commerce-failed = Operation declined. Check your ID card, permissions, balance and invoice version.
commerce-no-funds = Requires a linked station ID card and sufficient credits.
commerce-panel-open = Register service panel opened. A multitool can reassign ownership.
commerce-panel-closed = Register service panel closed.
commerce-hacked = Register protection bypassed. Your ID card now controls it.
commerce-price = { $price } cr.
commerce-print-price = Printing: { $price } cr. per document. Reserved on order; cancelled copies refunded.
commerce-print-failed = Order declined: check paper, quantity, price, your ID card and credit balance.
commerce-reserve = Item / printing payment reserved
commerce-refund = Payment reservation refunded
commerce-print-entry = Document printing → Service budget
commerce-vend-entry = Vending purchase → Service budget
commerce-purchase-entry = Payment: { $description }
ent-ImperialPaymentTerminal = payment terminal
    .desc = A portable register for goods and services. Revenue can go to a personal account or a department budget. Management requires the owner's ID card. Open its panel with a screwdriver and bypass protection with a multitool or emag.
ent-ImperialBarPaymentTerminal = bar register
    .desc = The bartender's register. Whoever controls its owner's ID, or hacks the device, sets the prices.
ent-ImperialKitchenPaymentTerminal = kitchen register
    .desc = The chef's register for selling meals and services.
