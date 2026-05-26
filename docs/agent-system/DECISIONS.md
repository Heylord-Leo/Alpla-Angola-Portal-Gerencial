
### DEC-121: Mandatory Receipt Upload for Finance
- **Date:** 2026-04-27
- **Context:** To ensure the portal aligns with the physical operational processes, a payment request cannot be fully considered 'Completed' until Finance has uploaded the official receipt document.
- **Decision:** Added a mandatory guardrail in the operational completion flow. The FinalizeRequest backend endpoint explicitly blocks requests in the WAITING_RECEIPT status from finalizing if a TYPE_RECEIPT ('Recibo') document is not attached. 
- **Implementation:** Added TYPE_RECEIPT attachment type. In the frontend, the UI action 'FINALIZAR PEDIDO' in WAITING_RECEIPT state is now exclusively visible to the Finance role. The receipt attachment component logic restricts this upload to Finance/System Admin users.
