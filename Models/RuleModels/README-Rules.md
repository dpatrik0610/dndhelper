# Rules API Guide

## Data shape
- Rule snippet: `{ id, slug, title, category, summary, tags[], updatedAt?, source? }`
- Rule detail adds: `body[], sources[], examples[], references[], relatedRuleSlugs[]`
- Categories (string): Core, Combat, Magic, Status, Equipment, Exploration, Downtime, Homebrew (or custom string)

## Endpoints
- `GET /api/rules?category=&tag=&source=&search=&cursor=&limit=` → list with `{ items, total, nextCursor }`
  - `category`: exact match
  - `tag`: exact match inside `tags[]`
  - `source`: case-insensitive match against `source.title`
  - `search`: full-text (title/summary/tags/body) with text index; falls back to regex if needed
  - `cursor`: base64 of `updatedAt|_id` for stable pagination; sort is `updatedAt desc, _id desc`
  - `limit`: default 20, max 100
- `GET /api/rules/{slug}` → `{ rule }` (full document)
- `GET /api/rules/stats` → `{ byCategory: { [category]: count }, topTags: [{ tag, count }], total }`
- Admin-only:
  - `POST /api/rules` (RuleDetail payload) → create
  - `PUT /api/rules/{slug}` (RuleDetail payload) → update

## Validation
- Required: `slug`, `title`, `summary`, `category`, `tags[]`
- `slug` unique; `updatedAt` auto-refreshed on create/update

## Sample cURL
```bash
curl "http://localhost:5000/api/rules?category=Core&tag=actions&search=bonus&limit=10"
curl "http://localhost:5000/api/rules/sample-action-economy"
curl "http://localhost:5000/api/rules/stats"

curl -X POST "http://localhost:5000/api/rules" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <ADMIN_TOKEN>" \
  -d '{
    "slug": "grappling",
    "title": "Grappling",
    "category": "Combat",
    "summary": "Rules for grappling",
    "tags": ["combat","grapple"],
    "body": ["When you take the Attack action, you can use one attack to grapple a creature within reach."]
  }'
```

## Seeding
- Optional placeholder seeder: call `RulesSeeder.SeedSampleRulesAsync(context, logger)` to insert sample data if the collection is empty. Replace with real data as needed.
