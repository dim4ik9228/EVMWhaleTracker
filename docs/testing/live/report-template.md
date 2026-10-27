# Live acceptance report — <run-id>

Date/time: <UTC start/end>
Request/scope: <selected case IDs; any explicit restart permission>
Target: <environment and bot identity; omit private chat IDs>
Revision: <deployed revision/image if known; otherwise unknown; note any dirty working tree>
Budget: <observation duration and alert stop threshold>

## Prerequisites and baseline

<Availability, selected public source and reason, private baseline location, temporary wallet ID, activation boundary. Never include credentials, keyed endpoints, private aliases or unrestricted logs.>

## Results

| Case / assertion | Result | Decisive evidence / observed limit |
| --- | --- | --- |
| <ID or subtype> | <Passed / Failed / Blocked / Inconclusive / Not run> | <source execution identity, observed Telegram facts, sanitized local artifact reference> |

## Cleanup

Result: <Passed / Failed / Blocked / Not needed>
Temporary wallet removed / protocol off: <evidence>
Pending deliveries: <none / cancelled / unresolved / not inspected; identify test wallet only>
Original baseline preserved: <comparison and access limitations>
Services/configuration: <unchanged or explicitly requested changes and final state>

## Remaining gaps

<Only unverified selected assertions, material defects and cleanup work. Historical source evidence is not a fresh live pass. Do not mark an entire feature verified from one alert.>
