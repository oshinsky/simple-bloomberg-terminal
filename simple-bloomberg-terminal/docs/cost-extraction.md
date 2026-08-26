# Cost extraction

COST extraction is counterparty-oriented. Workers read plain filing text and return named cost-side
relationships such as suppliers, vendors, manufacturers, foundries, licensors, and service providers.

Every candidate requires one verbatim excerpt that names the company and establishes the commercial
relationship. Values and percentages remain null unless the filing explicitly attributes a figure to
that named counterparty. Financial statements and tables are not parsed.

The shared flow is documented in [sec-extraction.md](sec-extraction.md).
