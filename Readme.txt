Instrucțiuni de execuție
1. Pornire DataWarehouse (BD Master și Slave)
Deschide 3 terminale separate pentru Master și Slaves.
1.1 Master
cd LL2/BD/DataWarehouse
dotnet run 8081 master
Master gestionează operațiuni: PUT, POST, DELETE.

1.2 Slave 1
cd LL2/BD/DataWarehouse
dotnet run 8082 slave
Slave 1 gestionează operațiuni GET și replică de la Master la fiecare 5 secunde.

1.3 Slave 2 (opțional)
cd LL2/BD/DataWarehouse
dotnet run 8083 slave
Slave 2 poate fi folosit pentru load balancing GET.

2. Pornire Proxy
Deschide un terminal nou:

cd LL2/Proxy/ProxyConsole
dotnet run
Proxy-ul ascultă pe: http://localhost:8080/
Direcționează cererile:
GET → Slave (ex: 8082, 8083)
PUT, POST, DELETE → Master (8081)
Caching activ pentru GET-uri (TTL 240 sec).
Dacă primești Address already in use, verifică să nu mai fie un alt Proxy activ pe port 8080.

3. Pornire User
Deschide un terminal nou:
cd LL2/User/UserConsole
dotnet run
Meniu interactiv:
Get Employee by ID (GET)
Get All Employees (GET)
Add New Employee (PUT)
Update Employee (POST)
Delete Employee (DELETE)
Exit
Toate cererile sunt trimise către Proxy (http://localhost:8080), care le redirecționează către DW corespunzător.

4. Demo funcționalitate
Pornire Master și cel puțin un Slave.
Pornire Proxy.
Pornire Client și adăugare angajați (PUT) → date scrise în Master.
Citire angajați (GET) → date preluate din Slave, cache activ.
Update/Delete angajați (POST/DELETE) → invalidate cache automat.
Observă replicarea datelor Master → Slave la 5 secunde.

5. Notes
Cache TTL: 240 secunde pentru GET-uri.
Cleanup periodic cache: la fiecare 60 secunde.
Proxy și DW trebuie să fie pornite înainte de client.
Slave nu poate scrie, Master nu poate citi.
