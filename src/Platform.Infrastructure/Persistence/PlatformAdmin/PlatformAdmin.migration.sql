-- Platform administration (intake LIS-INT-PLT-001): grants, the indexes EF cannot express, the cross-tenant count
-- function and the idempotent seed. Appended by the integrator to the wave migration after the saas tables exist;
-- plain SQL, safe to run twice (IF NOT EXISTS, CREATE OR REPLACE, ON CONFLICT DO NOTHING on natural keys).

-- 1. Grants: lis_app has no default privileges in saas (deploy/postgres/schema.sql).
GRANT USAGE ON SCHEMA saas TO lis_app;
GRANT SELECT, INSERT, UPDATE ON
    saas.tenants, saas.plans, saas.wallets, saas.wallet_transactions, saas.wallet_top_up_requests,
    saas.notification_templates, saas.analyser_models, saas.pcr_kit_templates, saas.code_systems, saas.code_system_items,
    saas.rule_templates, saas.subscription_requests
    TO lis_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON saas.countries, saas.governorates TO lis_app;   -- SA-10: an unreferenced country may be deleted

-- 2. INV-PLT-011: a governorate's English name is unique within its country (case-insensitive).
CREATE UNIQUE INDEX IF NOT EXISTS ux_governorates_country_name ON saas.governorates (country_id, lower(name_en));

-- 3. SA-04 branch and user counts across tenants without elevation: SECURITY DEFINER (owner lis_owner, BYPASSRLS),
--    counts only, and only for a session whose tenant is the platform tenant.
CREATE OR REPLACE FUNCTION saas.client_usage_counts(p_tenant_ids uuid[])
RETURNS TABLE (tenant_id uuid, branches bigint, users bigint)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $fn$
#variable_conflict use_column
BEGIN
    IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
        RAISE EXCEPTION 'saas.client_usage_counts is reserved to the platform tenant' USING ERRCODE = '42501';
    END IF;

    IF to_regclass('app.branches') IS NULL THEN
        RETURN QUERY
            SELECT t.id,
                   0::bigint,
                   (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
            FROM unnest(p_tenant_ids) AS t(id);
    ELSE
        RETURN QUERY EXECUTE
            'SELECT t.id,
                    (SELECT count(*) FROM app.branches b WHERE b.tenant_id = t.id),
                    (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
             FROM unnest($1) AS t(id)'
            USING p_tenant_ids;
    END IF;
END
$fn$;
REVOKE ALL ON FUNCTION saas.client_usage_counts(uuid[]) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION saas.client_usage_counts(uuid[]) TO lis_app;

-- 4. Seed: countries (SA-10), the governorates of Egypt (SA-11), plans (SA-05), notification templates (SA-20).
INSERT INTO saas.countries (id, name_en, name_ar, iso_code, dial_code, currency, currency_code, language, time_zone, mobile_length, flag, remarks, is_active, created_at_utc, created_by_display_name)
VALUES
    (md5('lis:country:EG')::uuid, 'Egypt', 'مصر', 'EG', '+20', 'Egyptian Pound', 'EGP', 'ar', 'Africa/Cairo', 10, '🇪🇬', NULL, true, now(), 'seed'),
    (md5('lis:country:SA')::uuid, 'Saudi Arabia', 'المملكة العربية السعودية', 'SA', '+966', 'Saudi Riyal', 'SAR', 'ar', 'Asia/Riyadh', 9, '🇸🇦', NULL, true, now(), 'seed'),
    (md5('lis:country:AE')::uuid, 'United Arab Emirates', 'الإمارات العربية المتحدة', 'AE', '+971', 'UAE Dirham', 'AED', 'ar', 'Asia/Dubai', 9, '🇦🇪', NULL, true, now(), 'seed'),
    (md5('lis:country:KW')::uuid, 'Kuwait', 'الكويت', 'KW', '+965', 'Kuwaiti Dinar', 'KWD', 'ar', 'Asia/Kuwait', 8, '🇰🇼', NULL, true, now(), 'seed'),
    (md5('lis:country:JO')::uuid, 'Jordan', 'الأردن', 'JO', '+962', 'Jordanian Dinar', 'JOD', 'ar', 'Asia/Amman', 9, '🇯🇴', NULL, true, now(), 'seed')
ON CONFLICT (iso_code) DO NOTHING;

INSERT INTO saas.governorates (id, country_id, name_en, name_ar, district, remarks, is_active, created_at_utc, created_by_display_name)
SELECT md5('lis:governorate:EG:' || g.name_en)::uuid, c.id, g.name_en, g.name_ar, NULL, NULL, true, now(), 'seed'
FROM saas.countries c
CROSS JOIN (VALUES
    ('Cairo', 'القاهرة'), ('Giza', 'الجيزة'), ('Alexandria', 'الإسكندرية'), ('Qalyubia', 'القليوبية'), ('Sharqia', 'الشرقية'),
    ('Dakahlia', 'الدقهلية'), ('Gharbia', 'الغربية'), ('Monufia', 'المنوفية'), ('Beheira', 'البحيرة'), ('Kafr El Sheikh', 'كفر الشيخ'),
    ('Damietta', 'دمياط'), ('Port Said', 'بورسعيد'), ('Ismailia', 'الإسماعيلية'), ('Suez', 'السويس'), ('North Sinai', 'شمال سيناء'),
    ('South Sinai', 'جنوب سيناء'), ('Faiyum', 'الفيوم'), ('Beni Suef', 'بني سويف'), ('Minya', 'المنيا'), ('Asyut', 'أسيوط'),
    ('Sohag', 'سوهاج'), ('Qena', 'قنا'), ('Luxor', 'الأقصر'), ('Aswan', 'أسوان'), ('Red Sea', 'البحر الأحمر'),
    ('New Valley', 'الوادي الجديد'), ('Matrouh', 'مطروح')
) AS g(name_en, name_ar)
WHERE c.iso_code = 'EG'
ON CONFLICT (country_id, lower(name_en)) DO NOTHING;

INSERT INTO saas.plans (id, code, name_en, name_ar, monthly_fee, currency_code, max_branches, max_users, sms_quota, max_analysers, is_active, created_at_utc, created_by_display_name)
VALUES
    (md5('lis:plan:STARTER')::uuid, 'STARTER', 'Starter', 'الباقة الأساسية', 1500.00, 'EGP', 1, 5, 500, 1, true, now(), 'seed'),
    (md5('lis:plan:STANDARD')::uuid, 'STANDARD', 'Standard', 'الباقة القياسية', 3500.00, 'EGP', 3, 20, 2000, 5, true, now(), 'seed'),
    (md5('lis:plan:ENTERPRISE')::uuid, 'ENTERPRISE', 'Enterprise', 'باقة المؤسسات', 9000.00, 'EGP', 20, 200, 10000, 50, true, now(), 'seed')
ON CONFLICT (code) DO NOTHING;

INSERT INTO saas.notification_templates (id, code, channel, subject_en, subject_ar, text_en, text_ar, is_active, created_at_utc, created_by_display_name)
VALUES
    (md5('lis:template:otp')::uuid, 'otp', 'Sms', NULL, NULL,
     'Your Sky LIS verification code is {code}. It is valid for {minutes} minutes. Never share it.',
     'رمز التحقق الخاص بك في Sky LIS هو {code}. صالح لمدة {minutes} دقائق. لا تشاركه مع أحد.', true, now(), 'seed'),
    (md5('lis:template:password-recovery')::uuid, 'password-recovery', 'Sms', NULL, NULL,
     'Use {code} to reset your Sky LIS password. The code expires in {minutes} minutes. Ignore this message if you did not ask for it.',
     'استخدم الرمز {code} لإعادة تعيين كلمة مرور Sky LIS. ينتهي الرمز خلال {minutes} دقائق. تجاهل هذه الرسالة إن لم تطلبها.', true, now(), 'seed'),
    (md5('lis:template:portal-url')::uuid, 'portal-url', 'Sms', NULL, NULL,
     '{lab}: your Sky LIS portal is ready at {url}. Sign in with the username {username}.',
     '{lab}: بوابة Sky LIS الخاصة بكم جاهزة على {url}. سجّلوا الدخول باسم المستخدم {username}.', true, now(), 'seed'),
    (md5('lis:template:treasury-request')::uuid, 'treasury-request', 'Sms', NULL, NULL,
     'Treasury access request {number}: {requester} asks for access to treasury {treasury}.',
     'طلب صلاحية الخزينة {number}: يطلب {requester} صلاحية على الخزينة {treasury}.', true, now(), 'seed'),
    (md5('lis:template:panic-value')::uuid, 'panic-value', 'Sms', NULL, NULL,
     'Panic value for {patient} (accession {acc}): {test} = {value}. Please act now.',
     'قيمة حرجة للمريض {patient} (رقم الطلب {acc}): {test} = {value}. يرجى التصرف فورًا.', true, now(), 'seed')
ON CONFLICT (code) DO NOTHING;
