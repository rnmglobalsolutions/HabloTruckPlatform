# HabloTruck Mega Prompt

Generated for reuse in other AI tools on 2026-04-13.
Updated with confirmed pricing on 2026-04-13.

Copy and paste everything below into any tool that needs to understand HabloTruck quickly and accurately.

---

## Mega Prompt

You are working on HabloTruck. Treat this document as the source-of-truth business and product context unless the user gives you newer information.

HabloTruck is a Spanish-first, chat-centered platform that helps truck drivers, CDL students, trucking companies, fleets, and driving schools get access to practical trucking English and related learning/support experiences. Its core promise is simple: help Spanish-speaking trucking workers learn the real English used on the road in the United States, while giving individuals, companies, and schools a low-friction way to buy, activate, verify, recover, and retain access through ManyChat, Stripe, and a serverless backend.

The product should feel practical, direct, supportive, and road-relevant. Avoid generic language-learning positioning. HabloTruck is not just "an English course." It is a trucking-specific access and learning platform designed around the realities of drivers, students, school admins, fleet operators, payment issues, invite codes, and chat-based onboarding.

North-star ambition:

HabloTruck should become the go-to solution for Latino truckers in America to learn road-truck English: the practical English used with dispatchers, shippers, receivers, brokers, mechanics, DOT officers, roadside inspectors, warehouses, customers, and other people drivers deal with on the job.

Core learning delivery format:

HabloTruck teaches road-truck English through compact chat messages that usually include four parts:

1. English phrase.
2. Spanish translation.
3. Cuban pronunciation guide.
4. Native English audio recording of the phrase.

The Cuban pronunciation guide is a practical sound bridge for the user. It is meant to show how the English phrase sounds using a familiar Cuban/Caribbean Spanish-friendly phonetic approximation, so the driver or student can repeat the phrase as close as possible to the native English sound. It is not academic IPA and should never be written in a mocking or stereotyped way. It should be respectful, useful, easy to repeat, and focused on the actual sound.

The native English recording is usually short, around 5-7 seconds maximum, and should model the phrase naturally and clearly.

Core product experience:

HabloTruck is a structured menu-based training system, not a random phrase generator. The user should feel they are browsing a practical trucking-English dashboard organized around the way a trucker actually works day to day. The main menu should expose operational modules, each module should feel like a mini-course, and the chat should keep the lesson loop fast: phrase, translation, Cuban pronunciation, audio, then Next or Repeat.

Confirmed core prices:

- Individual monthly: $12.99 USD plus applicable taxes.
- Individual yearly: $97 USD plus applicable taxes.
- Company, fleet, or school seat: $9.99 USD per seat per month plus applicable taxes.

## One-Sentence Definition

HabloTruck helps Latino and Spanish-speaking truck drivers and CDL students in America learn practical road-truck English through conversational onboarding, $12.99/month or $97/year individual subscriptions, $9.99/month company or school seats, invite codes, automated billing recovery, and retention flows.

## Short Elevator Pitch

HabloTruck gives truck drivers, students, companies, and schools an easy way to activate practical English learning for the U.S. trucking world. Individuals can subscribe monthly or yearly from chat. Companies and schools can buy seats for a team, receive an invite code, and let drivers or students claim access. Stripe handles checkout and billing, ManyChat handles the conversational experience, and the HabloTruck backend keeps access, seats, payments, reminders, and recovery states synchronized.

## Offer Summary

HabloTruck has a simple offer ladder:

- Individual monthly access for drivers/students: $12.99 USD/month plus taxes.
- Individual yearly access for drivers/students: $97 USD/year plus taxes.
- Company, fleet, or school seats: $9.99 USD per seat/month plus taxes.
- Optional CDL cohort SKUs can exist as one-time Stripe checkouts when needed, but their final pricing should be confirmed separately.

The commercial story should stay easy to understand: individuals buy their own access, while companies and schools buy seats and distribute access with invite codes.

## Free And Full Access Model

HabloTruck should have a clear free-to-paid path.

Free version:

- Gives users 5 phrases per day.
- Pulls those phrases from different trucking modules.
- Creates a daily habit instead of overwhelming the user.
- Lets users experience the core learning format: English phrase, Spanish translation, Cuban pronunciation, and short audio.
- Shows the broader module menu with locked modules.
- Creates curiosity and desire to explore specific topics such as Police, DOT Inspection, GPS, Weigh Station, Loads, Fuel, Parking, and BOL.

Free-version strategy:

- The free version is not just a sample. It is a habit builder and conversion engine.
- The user should feel: "This helps me, but I need full access for the exact situations I face at work."
- Locked modules should be visible enough to create interest, but not frustrating enough to feel deceptive.
- The free user should always have a simple upgrade path.

Full access:

- Unlocks 10+ structured operational modules.
- Includes deep, scenario-based trucking-English phrase libraries.
- Can reasonably be positioned as 1,000+ practical phrases if each core module contains around 100 phrases.
- Supports unlimited practice compared with the daily free limit.
- Keeps the same fast learning format: phrase, translation, Cuban pronunciation, and audio.
- Uses a menu-driven learning system with visual cards/images for intuitive navigation.

Full-access user experience:

- Main Menu = module selection screen.
- Each module = mini-course.
- Visual cards = easy, intuitive navigation.
- The experience should feel more like browsing useful work scenarios than taking a boring course.
- A good analogy is "Netflix-style browsing for trucking-English situations," but avoid overusing that publicly unless the design actually supports it.

Interaction loop:

- User selects a module.
- HabloTruck sends one phrase lesson.
- User listens to native audio.
- User can tap Repeat to hear/review it again.
- User can tap Next to continue.
- The flow continues inside the selected module.
- The user should always be able to return to the main menu.

## What HabloTruck Does

HabloTruck supports the full commercial and access lifecycle around a trucking-English learning product:

- Welcomes new users through a ManyChat entry router.
- Routes users based on intent and access state.
- Presents a menu-based training system organized by real trucking work situations.
- Supports a free daily phrase experience and a full-access module experience.
- Lets individuals buy monthly or yearly access.
- Lets company owners, fleet operators, and school admins buy seat bundles.
- Creates Stripe Checkout sessions for individual and company purchases.
- Receives Stripe webhook events as the source of truth for payments and subscriptions.
- Projects subscription state into effective access.
- Creates company records and seat entitlements after fleet purchases.
- Auto-generates invite codes for purchased company or school seats.
- Lets drivers or students validate invite codes and claim seats.
- Tracks seat usage and prevents over-capacity access.
- Synchronizes access state back to ManyChat through tags and custom fields.
- Supports payment-failure recovery through Stripe Billing Portal update links.
- Supports manual retry of open Stripe invoices.
- Sends renewal reminders.
- Supports "Stay with HabloTruck" retention flows before churn.
- Provides success, cancel, and billing-return status pages for Stripe redirects.
- Provides admin/support endpoints for invites, failed actions, Stripe replay, and operational recovery.

## Core Customer Segments

Primary B2C segment:

- Spanish-speaking truck drivers.
- CDL students.
- New drivers preparing to work in the United States.
- Drivers who need practical English for road, dispatch, loading, inspection, safety, billing, and job communication contexts.

Primary B2B segment:

- Trucking company owners.
- Fleet operators.
- Dispatch or operations managers buying training access for drivers.
- CDL schools or training programs buying access for students.
- Admins who need to distribute access through seats and invite codes.

Secondary/internal users:

- HabloTruck support/admin operators.
- ManyChat flow builders.
- Backend/API operators.
- Growth, sales, and retention teams.

## Market

HabloTruck operates at the intersection of:

- Trucking and logistics workforce training.
- English for specific purposes.
- Spanish-speaking immigrant and bilingual workforce enablement.
- CDL training and driver readiness.
- Fleet training and retention.
- Conversational commerce through chat automation.

The market opportunity is strongest where Spanish-speaking drivers, students, and operators need practical language help tied to trucking work rather than broad academic English. HabloTruck should speak to real road outcomes: better communication, confidence, fewer misunderstandings, smoother dispatch/load interactions, and stronger readiness for U.S. trucking environments.

## Problem It Solves Today In America

HabloTruck solves a real communication and workforce-readiness problem in the U.S. trucking industry: Spanish-speaking drivers and CDL students often need practical English for trucking situations, but most English learning options are too generic, too slow, too classroom-like, or not connected to the exact moments where language matters on the road.

In America today, trucking work is English-heavy in the places where mistakes are expensive:

- Road signs, traffic instructions, and safety warnings.
- Dispatch calls and load updates.
- Shipper and receiver instructions.
- Check-in, dock, yard, and warehouse communication.
- DOT, roadside inspection, and law-enforcement questions.
- Bills of lading, logs, incident notes, and basic records.
- Mechanical issue reporting.
- Delivery exceptions, delays, lumper/payment questions, and claims.
- Customer, broker, dispatcher, and operations communication.

This matters because commercial truck driving is a large, high-stakes occupation in the United States. The Bureau of Labor Statistics reports about 2.2 million heavy and tractor-trailer truck driver jobs in 2024 and projects hundreds of thousands of openings per year over the decade. The same BLS profile emphasizes that long-haul drivers may be away from home for days or weeks, work nights/weekends/holidays, and face one of the highest occupational injury and fatality risk profiles. In that environment, English is not just "nice to have." It affects confidence, safety communication, productivity, and opportunity.

There is also a regulatory-readiness angle. U.S. commercial driver qualification rules require drivers to have enough English ability to communicate with the public, understand highway signs/signals, respond to official inquiries, and make entries on reports and records. FMCSA guidance also tells motor carriers to assess whether drivers can communicate with enforcement officers and understand highway traffic signs. Since June 25, 2025, non-compliance with the English language proficiency rule can be treated as a driver out-of-service violation under the CVSA North American Standard Out-of-Service Criteria.

Important positioning guardrail:

HabloTruck should not claim to guarantee legal compliance, guarantee passing an inspection, guarantee employment, or replace official CDL training. The correct claim is that HabloTruck helps drivers and students build practical trucking-English readiness for the real communication moments they face in the U.S. trucking industry.

Useful source notes for factual context:

- U.S. Bureau of Labor Statistics, Heavy and Tractor-trailer Truck Drivers: https://www.bls.gov/ooh/transportation-and-material-moving/heavy-and-tractor-trailer-truck-drivers.htm
- eCFR 49 CFR 391.11(b)(2), General qualifications of drivers: https://www.law.cornell.edu/cfr/text/49/391.11
- FMCSA English Language Proficiency assessment guidance: https://www.fmcsa.dot.gov/regulations/what-should-motor-carrier-do-assess-cmv-drivers-english-language-proficiency-elp-during
- CVSA English Language Proficiency out-of-service update: https://cvsa.org/news/elp-oosc-06252025/

## Pain Caused By The Problem

For drivers and students, the pain is personal and practical:

- Anxiety when a dispatcher, officer, shipper, broker, or receiver speaks fast English.
- Fear of looking unprepared or being embarrassed in front of coworkers or officials.
- Missed or misunderstood instructions that can create delays, rejected loads, wrong turns, or extra waiting time.
- Trouble explaining mechanical issues, delays, incidents, or load problems.
- Stress during roadside inspections or official questions.
- Less confidence applying for better routes, better companies, or more independent work.
- Feeling stuck between knowing how to drive and not yet having the job-specific English to communicate confidently.

For companies, fleets, and schools, the pain is operational:

- Drivers may need extra help with basic English-heavy situations.
- Dispatchers and admins spend time clarifying repeated communication problems.
- Training teams may lack a practical, trucking-specific English layer.
- Schools can teach driving skills but still need a simple way to support English readiness for students.
- Companies need a scalable way to distribute learning access to many drivers without manually managing every account.
- Payment, onboarding, access, and renewal friction can turn a helpful training product into support workload if not automated.

HabloTruck addresses this pain by combining:

- Trucking-specific English content and positioning.
- A repeatable lesson-message format: English phrase, Spanish translation, Cuban pronunciation guide, and short native English audio.
- Spanish-first conversational onboarding.
- Simple individual pricing.
- Company/school seat bundles.
- Invite-code activation.
- Automated access verification.
- Billing recovery.
- Retention flows that keep users engaged before they disappear.

## Industry Expert Challenge: What The Prompt Must Not Miss

If you are using this prompt to build strategy, content, ads, scripts, flows, or product plans, do not reduce HabloTruck to "English lessons for truck drivers." That is too small.

The stronger framing is:

HabloTruck is a road-truck-English readiness platform for Latino truckers in America. It helps drivers handle the English moments that affect time, money, safety, confidence, compliance-readiness, and career opportunity.

Missing or underweighted pain to include:

- Time pressure: truck drivers often communicate in English while they are tired, under appointment pressure, trying to find parking, dealing with detention, or trying not to lose hours.
- Money pressure: miscommunication can contribute to delays, missed appointments, detention disputes, layovers, rejected loads, claims, unpaid waiting time, or fewer good opportunities.
- Safety pressure: drivers need to understand signs, warnings, inspection instructions, yard rules, receiver directions, mechanical issues, and emergency language.
- Dignity pressure: many drivers know how to work hard and drive safely but feel embarrassed, ignored, or talked down to when English becomes a barrier.
- Regulatory pressure: English language proficiency is not a new idea in U.S. commercial driver qualification rules, but enforcement attention increased in 2025. HabloTruck should position around readiness, not legal guarantees.
- Fleet pressure: carriers and schools do not just need content; they need a simple way to distribute access, reduce repeated communication problems, and support drivers without building a training department.
- School pressure: CDL schools can teach vehicle operation and test preparation, but many students still need practical English for the job environment after training.
- Retention pressure: drivers who feel unsupported may disengage from learning or leave the company/school relationship; HabloTruck can become a retention and support touchpoint.
- Trust pressure: Latino truckers need a product that feels built for them, not a generic app with Spanish translation added later.

Current U.S. trucking context that strengthens HabloTruck's case:

- ATRI's 2025 Top Industry Issues report says the economy remained the industry's top concern for the third consecutive year and that English Language Proficiency for Drivers entered the list as a first-time issue.
- Truck parking remains a national safety concern according to FHWA/Jason's Law materials; parking shortages can push drivers toward unsafe or unofficial parking locations.
- Driver detention remains a safety and earnings issue. DOT OIG research estimated that a 15-minute increase in average dwell time raises expected crash rate by 6.2 percent and that detention is associated with major annual driver earnings losses.
- FMCSA's Entry-Level Driver Training rules create a baseline for CDL training, but they do not remove the need for practical job-specific English practice after or alongside CDL training.
- U.S. commercial driver qualification rules require sufficient English to communicate with the public, understand highway traffic signs/signals, respond to official inquiries, and make entries on reports and records.

Useful source notes for this industry-challenge context:

- ATRI 2025 Top Industry Issues: https://truckingresearch.org/2025/10/for-the-third-year-in-a-row-the-economy-is-the-trucking-industrys-top-concern/
- FHWA Jason's Law Truck Parking Survey: https://ops.fhwa.dot.gov/freight/infrastructure/truck_parking/jasons_law/truckparkingsurvey/
- FMCSA Driver Detention research page: https://www.fmcsa.dot.gov/research-and-analysis/impact-driver-detention-time-safety-and-operations
- DOT OIG Driver Detention report: https://www.oig.dot.gov/library-item/36237
- FMCSA Entry-Level Driver Training: https://www.fmcsa.dot.gov/registration/commercial-drivers-license/entry-level-driver-training-eldt

## Problem-To-Solution Map

Use this map when explaining what HabloTruck solves.

Driver does not understand dispatcher/load instructions:

- HabloTruck solution: scenario-based trucking English, short phrase practice, Spanish translations, Cuban pronunciation guides, short native English recordings, and repeated exposure to real dispatch language.

Driver struggles at shipper/receiver:

- HabloTruck solution: pickup, delivery, check-in, dock, appointment, bill of lading, seal, detention, lumper, and warehouse communication modules delivered as phrase + translation + pronunciation + audio.

Driver is nervous during DOT/roadside inspection:

- HabloTruck solution: inspection roleplay, official-inquiry vocabulary, document phrases, vehicle/defect language, and calm practice scripts.
- Guardrail: do not claim HabloTruck guarantees inspection success or legal compliance.

Driver loses time because English-heavy situations create confusion:

- HabloTruck solution: practical micro-lessons for the moments where a driver needs to ask, confirm, clarify, or report a problem quickly.

Driver has pride but feels embarrassed asking basic questions:

- HabloTruck solution: Spanish-first, respectful tone that treats the driver as capable and experienced, not as a beginner in life.

CDL student learns vehicle operation but lacks job-specific English:

- HabloTruck solution: post-classroom and companion English layer for the real U.S. trucking workplace.

Company/school needs to support many drivers:

- HabloTruck solution: $9.99/seat/month access, company purchase flow, auto-created invite codes, seat assignment, access tracking, and ManyChat-based support paths.

Admin does not want another complicated LMS:

- HabloTruck solution: chat-first access, Stripe checkout, invite code distribution, and automated ManyChat state sync.

Payment failure interrupts learning:

- HabloTruck solution: grace periods, billing recovery, Stripe Billing Portal update links, retry-payment flow, reminders, and retention journeys.

User is active but gets accidentally resold:

- HabloTruck solution: access-state routing in ManyChat using backend-synchronized tags and fields.

## Road-Truck-English Content Pillars

These are the learning/content categories HabloTruck should own. They are important for product strategy, curriculum planning, ads, landing pages, ManyChat flows, and future app experiences.

HabloTruck content is situational training, not just vocabulary. A strong module should teach practical question-and-answer patterns, real phrases, and realistic workplace scenarios. Each core module can be designed around roughly 100 phrases, producing a full-access library of 1,000+ practical trucking-English phrases across 10+ modules.

Every phrase lesson should generally follow this message structure:

- English Phrase: the exact phrase the driver should learn.
- Spanish Translation: the meaning in natural Spanish.
- Cuban Pronunciation: a practical Cuban/Caribbean Spanish-friendly pronunciation guide that helps the user approximate the native English sound.
- Native English Recording: a short native-speaker audio clip, usually 5-7 seconds maximum.

Example structure:

- English Phrase: "Good Morning Officer"
- Spanish Translation: "Buenos dias Oficial"
- Cuban Pronunciation: "Gud Moornin Ofiser"
- Native English Recording: short native audio of "Good Morning Officer."

Another example:

- English Phrase: "I am checking in for pickup."
- Spanish Translation: "Estoy registrandome para recoger la carga."
- Cuban Pronunciation: "Ai am chequin in for pih-cop."
- Native English Recording: short native audio of "I am checking in for pickup."

Lesson-format guardrails:

- Keep the phrase practical and immediately usable.
- Keep the pronunciation guide respectful and sound-focused.
- Avoid making the pronunciation guide look like a joke or caricature.
- Keep audio short enough for drivers to replay quickly.
- Prefer one clear phrase per audio clip.
- If the phrase is safety-sensitive or compliance-related, include a simple caution or context note.

## Full Access Module Breakdown

Full Access should feel like a professional trucking-English training dashboard. These modules should be treated as core product categories:

1. Police and Dispatchers

- Talking to police during stops.
- Understanding officer commands.
- Communicating with dispatch.
- High-emotion, high-conversion module because fear and uncertainty around police/official communication is real.

2. DOT Inspection

- Inspection conversations.
- Document and compliance language.
- Officer interaction.
- Useful for inspection-readiness and confidence.
- Guardrail: say "prepare for inspection conversations" or "communicate with more confidence," not "guaranteed to pass inspections."

3. Cargas y Descargas / Loads

- Pickup conversations.
- Delivery conversations.
- Warehouse communication.
- Appointment handling.
- Live load/live unload.
- Drop and hook.

4. Combustible / Fuel

- Paying for fuel.
- Asking questions at truck stops.
- Understanding fuel receipts and instructions.
- Fuel card and pump problem language.

5. Weigh Station / Bascula

- Entering scales.
- Understanding instructions from officers.
- Scale house communication.
- Compliance dialogue.

6. BOL / Bill of Lading

- Understanding paperwork.
- Signing documents.
- Verifying load details.
- Seal number, trailer number, pickup number, delivery number.
- This is a high-value module because many drivers struggle with paperwork language.

7. Parking / Truck Stops

- Reserving parking.
- Asking for available spots.
- Overnight stay communication.
- Shower, fuel, repair, restroom, restaurant, and service desk language.

8. Control de Trafico / Traffic Control

- Traffic officer instructions.
- Road control situations.
- Emergency directions.
- Detours, blocked lanes, construction, and accident-scene language.

9. GPS and Directions

- Asking for directions.
- Understanding route instructions.
- Navigation issues.
- Wrong entrance, low bridge, restricted road, truck route, and turnaround language.

10. Entrega / Delivery

- Final delivery interaction.
- Delivery confirmations.
- Issue handling.
- Late delivery, damaged freight, missing product, appointment change, and proof-of-delivery language.

Expandable modules:

- Mechanical problems.
- Road emergencies.
- Border control.
- Customer communication.
- Recruiter/job interview English.
- ELD/logbook support conversations.
- Insurance, claims, and accident reporting basics.

Module design rules:

- Each module should feel like a real work situation.
- Each module should include both statements and questions.
- Question + answer format is valuable because real trucking conversations are interactive.
- Do not teach isolated vocabulary without context when a scenario phrase would be better.
- Prefer phrases a driver can say today at work.
- The menu should show locked modules to create curiosity, but full access should clearly unlock the practical depth.

Dispatch and load communication:

- Load assignment.
- Pickup and delivery appointment.
- Check calls.
- ETA updates.
- Delay explanations.
- Route changes.
- Broker/dispatcher clarification.

Shipper and receiver communication:

- Check-in and check-out.
- Dock door instructions.
- Live load/live unload.
- Drop and hook.
- Trailer number.
- Seal number.
- Bill of lading.
- Lumper payment.
- Detention and waiting time.
- Rejected load, shortage, overage, damage, OS&D.

Road, safety, and official communication:

- Road signs.
- Detours.
- Weigh stations.
- Scale house instructions.
- DOT/roadside inspection.
- Officer questions.
- Logbook/ELD basics.
- Medical card/CDL/registration/insurance document words.
- Warnings, citations, and out-of-service language.

Maintenance and breakdown:

- Flat tire.
- Air leak.
- Brake issue.
- Check engine light.
- Reefer problem.
- Temperature setting.
- Trailer lights.
- Roadside assistance.
- Tow/service call.

Emergency and incident communication:

- Accident report basics.
- Calling 911.
- Explaining location.
- Injury and damage vocabulary.
- Hazard, spill, fire, blocked lane.
- Weather and road condition language.

Money and operations:

- Fuel card.
- Scale ticket.
- Toll.
- Advance.
- Lumper fee.
- Detention request.
- Layover.
- Rate confirmation.
- Invoice/basic payment questions.

Career and professionalism:

- Applying for trucking jobs.
- Talking to recruiter.
- Safety meeting language.
- Company policy language.
- Respectful clarification phrases.
- Saying "I don't understand, please repeat" without losing confidence.

## Product And Curriculum Opportunities

The current backend proves the access, payment, entitlement, and ManyChat infrastructure. These ideas are natural product expansions, but should not be presented as already shipped unless confirmed:

- Audio and pronunciation practice for trucking phrases.
- Voice roleplays with dispatcher, shipper, receiver, officer, mechanic, and broker personas.
- Inspection-readiness simulations.
- Shipper/receiver check-in simulations.
- Flashcards and micro-lessons by route/job scenario.
- Certificate or progress reporting for schools/fleets.
- Company admin dashboard for seat usage and driver progress.
- Driver confidence score or readiness checklist.
- WhatsApp-first experience for drivers who prefer WhatsApp over Facebook/Instagram.
- Referral/ambassador program for Latino trucker communities.
- Content partnerships with CDL schools, dispatch training programs, recruiters, and trucking influencers.

Roadmap guardrail:

Do not confuse future opportunities with current capabilities. Current confirmed capabilities are strongest around Stripe checkout, subscriptions, company seats, invite codes, access projection, ManyChat sync, billing recovery, reminders, and retention flows.

## Value Proposition

For individual drivers and students:

- Get trucking-specific English instead of generic English lessons.
- Start from chat without navigating a complicated web app.
- Choose monthly access at $12.99/month plus taxes or yearly access at $97/year plus taxes.
- Pay through Stripe.
- Verify access from ManyChat after checkout.
- Recover access if a payment fails.
- Keep learning without needing support for every billing issue.

For companies, fleets, and schools:

- Buy multiple seats at $9.99 per seat/month plus taxes.
- Share one invite code with drivers or students.
- Let team members activate their own access.
- Track seat capacity through backend entitlement logic.
- Avoid manually creating accounts for every driver.
- Retrieve or resend active invite codes from ManyChat.
- Keep company access separate from individual subscriptions.

For HabloTruck operations:

- Stripe remains the billing source of truth.
- ManyChat is the user-facing conversational layer.
- Azure Functions backend owns access rules, entitlements, seat assignment, and recovery logic.
- Async queues reduce friction and avoid making users wait for every downstream ManyChat sync.
- Tags and fields in ManyChat create clean segmentation for sales, access, billing recovery, and retention.

## Product Positioning

Use positioning like:

- "The go-to road-truck-English solution for Latino truckers in America."
- "English for the road, built for truck drivers."
- "Learn the exact English you need to work as a truck driver in the U.S."
- "Communicate with confidence with police, DOT, dispatchers, shippers, receivers, and truck stops."
- "Practice the English that protects your time, money, safety, and confidence on the road."
- "Practical trucking English for Spanish-speaking drivers and students."
- "A chat-first way to activate and manage HabloTruck access."
- "Individual subscriptions and company seats for trucking English learning."
- "Built for drivers, fleets, and CDL schools that need simple access and real trucking context."

High-conversion emotional triggers:

- Fear: police stops, DOT inspection, weigh stations, official questions.
- Frustration: not understanding dispatch, warehouse staff, paperwork, GPS/directions, or truck stop instructions.
- Aspiration: becoming more independent, confident, professional, and ready for better work opportunities.
- Relief: being able to replay short native audio and practice privately without embarrassment.

Approved benefit framing:

- Helps drivers communicate with more confidence.
- Helps drivers prepare for real trucking conversations.
- Helps reduce confusion in high-pressure work situations.
- Helps companies and schools support drivers with practical English.

Use caution with:

- "Avoid fines."
- "Pass inspections."
- "Never get in trouble."
- "Guaranteed compliance."

Safer alternatives:

- "Prepare for DOT inspection conversations."
- "Understand common officer instructions."
- "Reduce communication mistakes."
- "Communicate more clearly in inspection and roadside situations."
- "Build confidence before high-pressure moments."

Avoid positioning like:

- Generic English app.
- Generic ESL course.
- Generic LMS.
- Corporate training platform without trucking specificity.
- Payment app.
- CRM-only bot.
- A one-flow chatbot.

## Tone and Brand Voice

The user-facing tone should be:

- Spanish-first when speaking to drivers/students.
- Clear and reassuring.
- Practical and benefit-oriented.
- Respectful of working people.
- Direct, not academic.
- Calm during payment or access issues.
- Confident but not hype-heavy.
- Helpful during retention, not desperate.

Good Spanish-language tone examples:

- "Bienvenido a HabloTruck. Aqui vas a aprender el ingles real que se usa en la carretera en Estados Unidos."
- "Tu acceso ya esta activo."
- "Te ayudo a resolverlo."
- "Si tienes una empresa o escuela, puedes activar acceso para tu equipo."
- "Perfecto. Si ya tienes un codigo de empresa o escuela, te ayudo a activarlo ahora mismo."

Avoid:

- Overly formal Spanish that feels academic.
- Fear-based sales.
- "Last chance" pressure.
- Generic "learn English fast" promises.
- Claims about guaranteed job outcomes unless the business explicitly approves them.

## Core Journeys

HabloTruck should be modeled as modular journeys, not one giant bot flow.

The learning experience should also be modular, not random. A user should enter through a router, choose free or full access, then browse modules that mirror real trucking situations.

The five commercial/access journeys are:

1. Individual subscription.
2. Company or school purchase.
3. Join with invite code.
4. Billing recovery and renewal.
5. Stay with HabloTruck / save before churn.

The main learning journeys are:

1. Free daily phrases.
2. Full access module browsing.
3. Module mini-course flow.
4. Next/repeat phrase loop.
5. Return to main menu.
6. Upgrade from locked module.

Recommended top-level ManyChat flow set:

1. Flow 0 - Entry Router.
2. Flow - Individual Subscription.
3. Flow - Company or School Purchase.
4. Flow - Join with Invite Code.
5. Flow - Billing Recovery.
6. Flow - Renewal Reminder.
7. Flow - Stay with HabloTruck.
8. Flow - Mi Cuenta.
9. Flow - Free Daily Phrases.
10. Flow - Full Access Module Menu.
11. Flow - Module Lesson Player.

## Entry Router

The entry router should quickly classify the user into the right path:

- New individual buyer.
- Company or school admin.
- Driver or student with invite code.
- Current paid subscriber.
- User in billing recovery or renewal state.

Recommended entry keywords:

- info
- inicio
- menu
- ya pague
- mi acceso
- mi codigo
- actualizar pago

Recommended first message:

"Bienvenido a HabloTruck.

Aqui vas a aprender el ingles real que se usa en la carretera en Estados Unidos.

Como quieres comenzar?"

Recommended buttons:

- Quiero acceso completo
- Soy empresa o escuela
- Ya tengo codigo

Routing rules:

- If the user has full access, send them to content, not back to sales.
- If the user is in billing recovery, send them to recovery before reselling.
- If the user has company-backed access, respect that path and avoid unnecessary individual upsell.
- If the user already joined a company, treat repeated join attempts as verification, not failure.
- If the user says they already paid, verify access first.

## Individual Subscription Journey

This journey is for monthly and yearly buyers.

Supported plan types:

- individual_monthly
- individual_yearly

Goal:

- Let a driver or student choose monthly or yearly access.
- Collect email and optional phone.
- Create a Stripe Checkout session.
- Send the user to Stripe.
- Let Stripe webhooks confirm payment.
- Project access in the backend.
- Sync access state back to ManyChat.
- Let the user verify access with "Ya pague" or "Verificar mi acceso."

Important implementation reality:

- The backend supports checkout and subscription projection.
- Stripe webhook events are the source of truth for final payment/access state.
- Backend synchronization to ManyChat primarily updates tags and custom fields.
- A dedicated visible success message after checkout may need to be handled in ManyChat through a verification flow.

Key endpoint:

POST /api/stripe/payment-link

Expected monthly payload pattern:

{
  "planType": "individual_monthly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}

Expected yearly payload pattern:

{
  "planType": "individual_yearly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}

Successful response shape:

{
  "result": true,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_...",
  "generatedAtUtc": "2026-04-10T03:12:45.1234567Z",
  "error": null
}

After checkout:

- Stripe sends checkout/subscription events.
- Backend resolves or creates the user.
- Backend stores Stripe customer and subscription references.
- Backend computes effective access.
- Backend queues ManyChat access synchronization.
- ManyChat should offer "Ya pague" / "Verificar mi acceso."

## Company, Fleet, and School Seat Journey

This journey is for:

- Company owners.
- School admins.
- Fleet operators.
- Anyone buying access for a team.

Core B2B model:

- A company or school admin buys a bundle of seats.
- The backend creates or updates a Company.
- The backend creates or updates an Entitlement with SeatsTotal.
- The backend auto-creates an active invite code.
- Drivers or students use that invite code to claim one seat.
- The backend assigns seats, prevents over-capacity access, and syncs access back to ManyChat.

Key concepts:

- Company: the B2B customer account or tenant.
- Entitlement: the concrete package of seats owned by that company.
- InviteCode: the code that allows drivers/students to claim a seat.
- SeatAssignment: the record linking a user to a seat within an entitlement.

Key endpoint for admin purchase:

POST /api/company/fleet/start-checkout

Payload pattern:

{
  "companyName": "{{cf_company_name}}",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "seats": {{cf_requested_seats}},
  "successUrl": "https://<static-website-host>/company-success.html",
  "cancelUrl": "https://<static-website-host>/company-cancel.html"
}

Success response shape:

{
  "ok": true,
  "companyId": "C_01JQ...",
  "companyName": "Acme Trucking",
  "seats": 20,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_..."
}

After company checkout:

- Stripe confirms the purchase.
- Backend creates or updates company.
- Backend creates or updates entitlement.
- Backend stores seat count.
- Backend auto-creates active invite code.
- Admin returns to ManyChat to retrieve the code.

Key endpoint to retrieve active invite:

POST /api/company/invite/active

Payload:

{
  "companyId": "{{cf_company_id}}"
}

Response shape:

{
  "ok": true,
  "code": "HT-AB12CD",
  "companyId": "C1",
  "entitlementId": "E1",
  "status": "active",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16,
  "expiresAtUtc": "2026-04-30T00:00:00.0000000Z"
}

Key endpoint to resend/recover invite:

POST /api/company/invite/resend

Payload:

{
  "companyId": "{{cf_company_id}}",
  "entitlementId": "{{cf_entitlement_id}}"
}

Behavior:

- Return existing active invite if present.
- Recreate one from entitlement if missing.

## Driver or Student Invite-Code Journey

This journey is for drivers or students who already received a company/school code.

Goal:

- Ask for invite code.
- Validate the code.
- Collect or confirm identity.
- Join company.
- Assign one seat.
- Sync access to ManyChat.
- Route user into full-access HabloTruck experience.

Key endpoint to validate invite:

POST /api/company/invite/info

Payload:

{
  "inviteCode": "{{cf_invite_code}}"
}

Success response shape:

{
  "ok": true,
  "valid": true,
  "code": "HT-AB12CD",
  "status": "active",
  "companyId": "C1",
  "entitlementId": "E1",
  "expiresAtUtc": "",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16
}

Key endpoint to join:

POST /api/company/join

Payload:

{
  "inviteCode": "{{cf_invite_code}}",
  "email": "{{contact.email}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatChannel": "facebook"
}

Success response shape:

{
  "ok": true,
  "alreadyJoined": false,
  "companyId": "C1",
  "entitlementId": "E1"
}

Important business outcomes:

- alreadyJoined=true is a success, not an error.
- Users already in another company should be blocked and routed to support.
- Invite expired, disabled, exhausted, or no seats available should show retry/support options.
- Seat assignment conflicts should be handled calmly and escalated if repeated.

## Billing Recovery and Renewal

HabloTruck supports billing recovery because subscription access should not collapse into manual support every time a card fails.

Billing recovery covers:

- Failed payments.
- Delinquent subscriptions.
- Renewal reminders.
- Payment recovery follow-up.
- Self-service payment method update.
- Manual retry of an open Stripe invoice.
- Stay with HabloTruck retention.

Backend capabilities:

- Detect payment failure from Stripe webhook.
- Project subscription state.
- Move users into grace or blocked state based on access rules.
- Trigger a ManyChat payment failed flow if configured.
- Sync billing recovery tags and fields.
- Create Stripe Billing Portal links.
- Retry open Stripe invoices.
- Queue outbound ManyChat side effects asynchronously.

Key endpoint for payment method update:

POST /api/stripe/subscription/payment-method-update-link

Payload:

{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123",
  "returnUrl": "https://<static-website-host>/billing-return.html"
}

Success response shape:

{
  "result": true,
  "url": "https://billing.stripe.com/...",
  "sessionId": "bps_123",
  "customerId": "cus_123",
  "subscriptionId": "sub_123",
  "immediateRetryRecommended": true,
  "error": null
}

Key endpoint for retrying open invoice:

POST /api/stripe/subscription/retry-payment

Payload:

{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123"
}

Success response shape:

{
  "result": true,
  "customerId": "cus_123",
  "subscriptionId": "sub_123",
  "invoiceId": "in_123",
  "invoiceStatus": "open",
  "collectionMethod": "charge_automatically",
  "invoiceFound": true,
  "paymentAttempted": true,
  "invoicePaid": false,
  "error": null
}

Recommended recovery message:

"No pudimos procesar tu pago, pero tu acceso todavia puede recuperarse."

Buttons:

- Actualizar metodo de pago
- Intentar cobro otra vez
- Hablar con soporte

Recovered-state message:

"Listo. Tu pago fue corregido y tu cuenta quedo recuperada."

## Stay with HabloTruck Retention

This is the save-before-churn journey. It should be used when:

- Renewal is approaching.
- User looks at cancellation-related content.
- User seems at risk of churn.
- Backend triggers a reminder journey configured as save_before_churn.

Goal:

- Reinforce value before the user leaves.
- Route billing problems to recovery.
- Route hesitation/value concerns back to useful content.
- Offer support without sounding desperate.

Tone:

- Helpful.
- Practical.
- Benefit-oriented.
- Not generic.
- Not overly salesy.

Recommended message:

"Antes de irte, quiero recordarte todo lo que ya tienes en HabloTruck."

Buttons:

- Quiero seguir con HabloTruck
- Actualizar pago
- Hablar con soporte

## Access Model

Effective access can be:

- Full
- Grace
- Blocked

Access source can be:

- Individual
- Company
- Both
- None

Individual access:

- Full when the user's Stripe subscription is active/trialing/paid-through.
- Grace when payment fails or subscription ends but grace is still active.
- Blocked when no valid paid-through, active, company, cohort, or grace state exists.

Company access:

- Full when the user has an active seat and the entitlement is active.
- Grace when a company entitlement expired but remains within company grace.
- Blocked when no active entitlement/seat or grace applies.

Grace policies:

- Individual grace policy is configured in hours. Current environment parameter: 72 hours.
- Company grace policy is configured in days. Current environment parameter: 7 days.

Access decision priority:

- If company and individual/cohort are active, source is Both.
- If company active, source is Company.
- If individual or cohort active, source is Individual.
- If grace exists on both, source is Both and later grace end is shown.
- If only company grace exists, source is Company.
- If only individual grace exists, source is Individual.
- Otherwise, access is Blocked.

## ManyChat Tags and Fields

Backend-synchronized access tags:

- HT_ACCESS_FULL
- HT_ACCESS_GRACE
- HT_ACCESS_BLOCKED
- HT_SRC_INDIVIDUAL
- HT_SRC_COMPANY

Backend-synchronized billing tags:

- HT_BILLING_ACTION_REQUIRED
- HT_BILLING_RECOVERED

Backend-synchronized custom fields:

- ht_access_mode
- ht_grace_ends_utc
- ht_company_id
- ht_billing_recovery_status
- ht_billing_recovery_subscription_id
- ht_billing_recovery_invoice_id
- ht_billing_recovery_invoice_status
- ht_billing_recovery_updated_utc
- ht_billing_recovery_started_utc

ManyChat helper fields:

- cf_email
- cf_phone_e164
- cf_plan_type
- cf_checkout_url
- cf_checkout_session_id
- cf_last_checkout_error
- cf_company_id
- cf_company_name
- cf_requested_seats
- cf_entitlement_id
- cf_invite_code
- cf_last_company_error
- cf_actor_user_pk
- cf_actor_user_id
- cf_subscription_id
- cf_last_billing_error
- cf_payment_method_update_url

Built-in ManyChat values:

- {{contact.id}} as manyChatSubscriberId
- {{contact.email}} as email when available

ManyChat channels may include:

- facebook
- instagram
- whatsapp

## Pricing Model

Use these confirmed customer-facing prices unless the user provides newer pricing:

- Individual monthly: $12.99 USD per month plus applicable taxes.
- Individual yearly: $97 USD per year plus applicable taxes.
- Company, fleet, or school seat: $9.99 USD per seat per month plus applicable taxes.

Pricing architecture:

- Individual monthly subscription at $12.99/month + taxes.
- Individual yearly subscription at $97/year + taxes.
- Fleet/company/school monthly per-seat subscription at $9.99/seat/month + taxes.
- Optional one-time CDL cohort checkout SKUs. Confirm final amount before publishing cohort pricing.
- Optional testing price ID for non-production/testing flows.

Pricing strategy notes:

- The yearly plan is the better-value individual option compared with paying monthly for 12 months.
- Company/school pricing is per active seat and should be explained as scalable team access.
- Taxes are handled by Stripe checkout and should be described as "plus applicable taxes."
- Do not quote CDL cohort prices unless the business confirms a specific cohort SKU and amount.
- Free access should be positioned as 5 daily phrases.
- Full access should be positioned as unlimited access to 10+ structured modules and 1,000+ practical trucking-English phrases when the content library supports that count.
- Locked modules should create a clear upgrade path from free to paid.

Supported checkout plan types:

- individual_monthly
- individual_yearly
- fleet
- fleet_seat
- company_seat
- fleet_monthly
- cdl_cohort_25
- cdl_cohort_50
- cdl_cohort_100
- cdl_english_cohort
- testing

Dev environment Stripe price IDs currently present:

- Individual monthly: price_1TKiQGLkH66TmATPpKGUzJ2y
- Individual yearly: price_1TKiQGLkH66TmATP9GQyrk44
- Fleet seat monthly: price_1TBMGSLkH66TmATPQyx42A2k
- Testing: price_1T9I5wLkH66TmATPaVHICsmU

Production price IDs are currently left blank in the parameter file and should be supplied from the production Stripe account/configuration.

Recommended pricing copy:

- "Plan mensual: $12.99 USD al mes + taxes."
- "Plan anual: $97 USD al ano + taxes."
- "Empresa o escuela: $9.99 USD por seat al mes + taxes."
- "Cohortes CDL: precio unico por cohorte, confirmar SKU y monto en Stripe."

## Checkout and Redirect Pages

HabloTruck includes a static checkout-status website with pages for:

- Individual payment success.
- Individual payment cancel/not completed.
- Company checkout success.
- Company checkout cancel/not completed.
- Billing update return.
- 404/status home.

These pages are bilingual in English and Spanish and are used as Stripe Checkout and Billing Portal redirect destinations.

Operational note:

- successUrl, cancelUrl, and billing returnUrl should use the deployed static website host or another host explicitly allowed by Stripe__AllowedCheckoutRedirectHosts.
- Redirect URLs must use HTTPS unless local/development behavior is explicitly changed.

## Backend/API Architecture

The backend is a .NET Azure Functions app with Domain, Application, Infrastructure, and Functions layers.

Core integrations:

- Stripe for checkout, subscriptions, invoices, webhooks, billing portal, and pricing.
- ManyChat for conversational onboarding, tags, custom fields, and triggered flows.
- Azure Table Storage for users, companies, entitlements, invite codes, seat assignments, Stripe event records, failed actions, reminders, and indexes.
- Azure Queue for asynchronous ManyChat dispatch.
- Application Insights / Log Analytics for telemetry and operational monitoring.
- Bicep infrastructure for dev/prod Azure deployments.

Important backend function areas:

- Stripe payment link creation.
- Fleet checkout creation.
- Stripe webhook processing.
- Company invite creation/lookup/resend/disable/list.
- Company join.
- Payment method update link.
- Retry open invoice.
- Cancel subscription at period end.
- Stripe replay.
- Failed action listing/requeue.
- Timers for reminders, reconciliation, failed action retry, entitlement expiry, entitlement recount, grace sweeps, and ManyChat dispatch queue processing.

## Operational Principles

- Stripe webhook projection is the source of truth after payment.
- ManyChat should verify access after checkout instead of assuming immediate success.
- Access sync and reminder sends are queued asynchronously.
- Company purchases auto-create invite codes.
- Invite codes consume uses and seat capacity.
- Seat assignment should be idempotent for users already in the same company.
- Users already in another company should be blocked and routed to support.
- Failed payments should start recovery, not immediate harsh churn.
- Retention should be its own journey, not just a billing error flow.
- Company access should not be confused with individual upsell.

## Known Gaps or Cautions

- Production Stripe price IDs are blank in the checked parameter file.
- CDL cohort pricing is not confirmed in this prompt; confirm SKU and amount before publishing cohort offers.
- Individual checkout success is primarily synchronized through tags/fields; ManyChat should provide a visible "verify my access" path.
- Some admin experiences may still benefit from richer UX, such as invite lookup by admin identity or managing multiple entitlements.
- Avoid claiming the platform has a full web learning UI unless newer product context confirms it. The backend and docs strongly establish access, billing, ManyChat, and entitlement infrastructure around a trucking-English learning experience.

## Recommended User-Facing Copy Themes

For drivers/students:

- "Aprende el ingles real que se usa en la carretera."
- "Activa tu acceso y empieza desde aqui."
- "Si ya pagaste, verifico tu acceso."
- "Si tienes codigo de empresa o escuela, te ayudo a activarlo."
- "Tu acceso ya esta activo."

For companies/schools:

- "Activa acceso para tu equipo."
- "Compra seats para tus drivers o estudiantes."
- "Despues del pago, te damos un codigo para compartir."
- "Cada driver usa el codigo y ocupa un seat."
- "Puedes volver a ver o reenviar tu codigo desde ManyChat."

For billing recovery:

- "Te ayudo a resolver el pago."
- "Actualiza tu metodo de pago."
- "Intentar cobro otra vez."
- "Verificar estado."
- "Tu cuenta quedo recuperada."

For retention:

- "Antes de irte, revisemos lo que ya tienes en HabloTruck."
- "Puedes seguir, actualizar pago o hablar con soporte."
- "HabloTruck esta pensado para ayudarte con el ingles real del trabajo."

## What Other Tools Should Do With This Context

When generating strategy, copy, designs, flows, API docs, pitch decks, landing pages, sales scripts, support scripts, or product plans for HabloTruck:

- Preserve the north-star ambition: HabloTruck should become the go-to solution for Latino truckers in America to learn practical road-truck English.
- Keep trucking-specific English at the center.
- Preserve the core lesson-message format: English phrase, Spanish translation, Cuban pronunciation guide, and 5-7 second native English audio.
- Preserve the product architecture: free daily phrases, locked modules, full-access module menu, mini-courses, and Next/Repeat interaction loop.
- Treat HabloTruck as situational job training, not a random phrase bot.
- Treat ManyChat as the primary conversational UX.
- Treat Stripe as the billing source of truth.
- Treat the backend as the authority for access, entitlements, seats, and recovery state.
- Include both B2C individual plans and B2B company/school seats.
- Include the core full-access modules: Police and Dispatchers, DOT Inspection, Loads, Fuel, Weigh Station, BOL, Parking/Truck Stops, Traffic Control, GPS/Directions, and Delivery.
- Include invite-code activation as a core product feature.
- Include billing recovery and retention as first-class product features.
- Use the confirmed prices: $12.99/month individual, $97/year individual, and $9.99/seat/month for company/school seats, all plus applicable taxes.
- Do not invent CDL cohort pricing.
- Keep Spanish-first driver copy practical and direct.
- Avoid generic language-learning claims.
- Avoid routing active users back to purchase.
- Include "verify access" after checkout.
- Include support fallbacks for payment, invite, and seat conflicts.

## Compact Context Block

HabloTruck is a Spanish-first, chat-centered trucking-English platform with a clear north-star ambition: become the go-to solution for Latino truckers in America to learn practical road-truck English. It is a structured, menu-based training system, not a random phrase bot. Free users get 5 daily phrases and see locked modules; full-access users unlock 10+ operational modules and, when the content library supports it, 1,000+ practical phrases. Core modules include Police and Dispatchers, DOT Inspection, Loads, Fuel, Weigh Station, BOL, Parking/Truck Stops, Traffic Control, GPS/Directions, and Delivery. Its core lesson message gives users an English phrase, Spanish translation, Cuban pronunciation guide, and short native English recording, usually 5-7 seconds maximum, with Next and Repeat interaction. It serves drivers, CDL students, fleets, trucking companies, and schools. It sells individual access at $12.99 USD/month + taxes or $97 USD/year + taxes, and company/school seats at $9.99 USD per seat/month + taxes. It sells through Stripe, routes users through ManyChat, creates invite codes for purchased seats, lets drivers/students join with those codes, assigns seats, computes Full/Grace/Blocked access, syncs access and billing state back to ManyChat, handles payment failure recovery with Billing Portal links and invoice retries, sends renewal reminders, and includes a "Stay with HabloTruck" retention journey. The product promise is practical English for the U.S. trucking road, not generic English. The backend defines plan types and Stripe price IDs, including individual monthly, individual yearly, fleet seat monthly, optional CDL cohort SKUs, and testing.
