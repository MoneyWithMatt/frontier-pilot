# Parking stand placement spike
Initial implementation, preserved before the Airport Layout Editor milestone:
- Up to eight numbered GA stands, edited with runway-relative forward/right metre offsets, relative heading and radius.
- Core separation/bounds validation, deterministic TaxiwayParking XML, WPF property grid/preview and dry-run integration.
- 239 tests passed; generated two-stand airport XML passed the installed SDK 1.2.4 bglcomp.xsd validation.
- No MSFS/Package Tool execution or real Community writes. Parking spawn/terrain behaviour is not yet proven.
- SDK reference: https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Taxiway_XML_Properties.htm
- Evidence: evidence/parking-stands/. The upcoming editor supersedes runway-relative storage with local east/north coordinates while retaining the parking generation and validation.
User separately confirmed the live runway-only WPF creation workflow worked before requesting parking. No airport identifier or detailed per-start observations were supplied.
