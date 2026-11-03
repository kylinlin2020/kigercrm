-- 5.9.5: add an optimistic-concurrency fulfillment ledger to CRM sales-order lines.
-- Draft shipment quantities are reserved; dispatch moves reservations to shipped quantities.
-- Existing orders start with zero reserved and shipped quantities.

ALTER TABLE IF EXISTS crm_sales_order_line
    ADD COLUMN IF NOT EXISTS reserved_quantity numeric(18,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS shipped_quantity numeric(18,4) NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = current_schema()
          AND table_name = 'crm_sales_order_line'
    ) AND NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_crm_sales_order_line_fulfillment_qty'
          AND conrelid = 'crm_sales_order_line'::regclass
    ) THEN
        ALTER TABLE crm_sales_order_line
            ADD CONSTRAINT ck_crm_sales_order_line_fulfillment_qty
            CHECK (
                reserved_quantity >= 0
                AND shipped_quantity >= 0
                AND reserved_quantity + shipped_quantity <= quantity
            );
    END IF;
END
$$;
