## Epic 1: High-Performance Ingestion Engine

**Goal:** Build the "Librarian" system to parse, encode, and store energy trading BDD steps.[cite: 1, 3]

### **User Story 1.1: Structured Data Parsing**

As a developer, I want to parse `.feature` and `.xlsx` files into clean Markdown so that technical context is preserved for the LLM.[cite: 3]

* **Task 1:** Integrate **MarkItDown** for Excel-to-Markdown conversion.[cite: 3]
* **Task 2:** Build a Gherkin parser to extract individual Given/When/Then steps.[cite: 3]
* **Acceptance Criteria:**
  * Parser handles nested tables in Excel without data loss.[cite: 3]
  * Output is a standardized JSON format ready for embedding.[cite: 3]
* **Story Points:** 3

### **User Story 1.2: Lightweight Semantic Embedding**

As a system, I want to use **Model2Vec** to encode steps so that I can perform sub-millisecond searches while saving RAM for the LLM.[cite: 1, 3]

* **Task 1:** Implement **Model2Vec** distillation on the energy trading step library.[cite: 1, 3]
* **Task 2:** Configure **ChromaDB** to store vectors with domain metadata (e.g., Market Type).[cite: 2, 3]
* **Acceptance Criteria:**
  * Embedding process consumes less than 100MB of RAM.[cite: 1, 3]
  * Search latency is $< 10\text{ms}$ for a library of 1,000 steps.[cite: 3]
* **Story Points:** 5



**User Story 1.3: Step Definition Binding** As a developer, I want to index C# step definitions so that the RAG system understands the link between natural language and executable code.

- **Task 1:** Implement a **Regex or Roslyn-based parser** to extract strings from `[Given]`, `[When]`, and `[Then]` attributes in `.cs` files.

- **Task 2:** Create a "Binding Map" in **ChromaDB** that links a vector embedding to both the `.feature` text and its `.cs` method.

- **Acceptance Criteria:**
  
  - System identifies which steps are "Implemented" vs. "Unimplemented".
  
  - C# code snippets are stored as metadata and retrieved during "Gap Analysis".

- **Story Points:** 5

---

## Epic 2: Stateful Inference & Gap Analysis

**Goal:** Implement the **LangGraph** orchestrator to handle queries and generate new BDD logic.[cite: 2, 3]

### **User Story 2.1: Hybrid Search & RRF Fusion**

As a user, I want search results to combine semantic meaning and exact keyword matches (e.g., "Intraday") to ensure technical precision.[cite: 3]

* **Task 1:** Implement **BM25** keyword indexing on the local library.[cite: 3]
* **Task 2:** Build the **Reciprocal Rank Fusion (RRF)** logic to combine Vector and BM25 scores.[cite: 3]
* **Acceptance Criteria:**
  * Specific trading codes (EICs) are prioritized over similar-sounding semantic matches.[cite: 3]
  * Search returns a ranked list of the top 5 relevant steps.[cite: 3]
* **Story Points:** 5

### **User Story 2.2: Self-Correcting LLM Generation**

As a system, I want to route Gemma 4's output through a **Linter Node** so that invalid Gherkin syntax is automatically corrected.[cite: 2, 3]

* **Task 1:** Integrate **Gemma 4 (26B-A4B)** via Ollama with a 4,000-token context cap.[cite: 3]
* **Task 2:** Build a regex-based Linter Node to verify Given/When/Then keywords.[cite: 3]
* **Task 3:** Implement a LangGraph "Retry" loop for failed linting.[cite: 2, 3]
* **Acceptance Criteria:**
  * Failed steps are sent back to the LLM with specific error logs.[cite: 3]
  * System terminates after 3 failed attempts to prevent infinite loops.[cite: 3]
* **Story Points:** 8

---

## Epic 3: Security & Audit Resilience (STRIDE)

**Goal:** Harden the application against local and network-level threats per ADR 005.[cite: 3]

### **User Story 3.1: Data Integrity & Tamper Protection**

As a security officer, I want the system to verify library integrity on startup to prevent malicious data tampering.[cite: 3]

* **Task 1:** Implement **SHA-256 checksum** validation for all `.db` and `.feature` files.[cite: 3]
* **Task 2:** Build a "Lockdown" mode that prevents startup if a mismatch is detected.[cite: 3]
* **Acceptance Criteria:**
  * System fails to load if a single character in the source library is altered.[cite: 3]
* **Story Points:** 3

### **User Story 3.2: Traceability & Repudiation Logging**

As an administrator, I want an encrypted audit log of every "Accepted" step to identify who approved specific trading logic.[cite: 3]

* **Task 1:** Build an append-only **SQLite** audit logger (ADR 005).[cite: 3]
* **Task 2:** Implement AES-256 encryption for the audit database file.[cite: 3]
* **Acceptance Criteria:**
  * Logs capture Timestamp, User, Intent, and the Final BDD Step.[cite: 3]
  * Log entries are immutable once written.[cite: 3]
* **Story Points:** 5

---

## Epic 4: Professional Frontend & SSE Streaming

**Goal:** Build the React-based "Split-View" scenario editor.[cite: 3]

### **User Story 4.1: Real-Time Suggestion Streaming**

As a trader, I want to see the BDD steps being generated in real-time so that I don't have to wait for the entire response to complete.[cite: 3]

* **Task 1:** Configure **FastAPI SSE** (Server-Sent Events) for the generation stream.[cite: 3]
* **Task 2:** Build the React "Split-View" UI to render intent on the left and BDD on the right.[cite: 3]
* **Acceptance Criteria:**
  * Tokens appear in the UI as they are generated by Gemma 4.[cite: 3]
  * UI highlights "Gaps" (newly generated steps) in a different color.[cite: 3]
* **Story Points:** 5

---

## Project Velocity Summary

| Epic                          | Total Story Points | Priority            |
|:----------------------------- |:------------------:|:------------------- |
| **1. Ingestion Engine**       | 8                  | High (Foundation)   |
| **2. Inference Orchestrator** | 13                 | High (Core Logic)   |
| **3. Security & Audit**       | 8                  | Medium (Compliance) |
| **4. Frontend & SSE**         | 5                  | Medium (Usability)  |

**Total Estimate:** 34 Story Points[cite: 3]
