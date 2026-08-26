# Extraction pipeline

The current extraction pipeline is filing-text based:

```text
primary SEC filing
  -> plain-text Item and heading chunks
  -> parallel fast workers
     -> COST / REVENUE: named counterparties
     -> RISK: disclosed risks
  -> findings digest
     -> conversational chat
     -> repeated COST measurement
```

The pipeline does not parse financial tables or fetch structured financial-fact feeds. See
[sec-extraction.md](sec-extraction.md) and [master-architecture.md](master-architecture.md).
