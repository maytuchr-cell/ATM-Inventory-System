// กำหนด URL ของ Backend (พอร์ต 5128 ตามที่คุณใช้งานอยู่)
const apiUrl = 'http://localhost:5128/api/ticket';

// 1. ฟังก์ชันโหลดข้อมูลทั้งหมด (READ)
async function loadTickets() {
    const tableBody = document.getElementById('ticketTableBody');
    try {
        const response = await fetch(apiUrl);
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const tickets = await response.json();
        tableBody.innerHTML = ''; // ล้างข้อความ "กำลังโหลดข้อมูล..."

        // กรณีไม่มีข้อมูล
        if (tickets.length === 0) {
            tableBody.innerHTML = '<tr><td colspan="5" style="text-align: center;">ไม่มีข้อมูลตั๋วในระบบ</td></tr>';
            return;
        }

        tickets.forEach(ticket => {
            // กำหนดสีของสถานะ
            let statusClass = ticket.status === 'Approved' ? 'status-approved' : 'status-pending';
            
            // สร้างปุ่ม อนุมัติ (โชว์เฉพาะตอน Pending) และปุ่ม ลบ
            let actionButtons = '';
            if (ticket.status === 'Pending') {
                actionButtons += `<button class="btn-approve" onclick="approveTicket(${ticket.ticketId})">✔️ อนุมัติ</button> `;
            }
            actionButtons += `<button class="btn-delete" onclick="deleteTicket(${ticket.ticketId})">🗑️ ลบ</button>`;

            // สร้างแถวข้อมูลในตาราง
            let row = `<tr>
                <td>${ticket.ticketId}</td>
                <td>${ticket.technicianName}</td>
                <td>${ticket.partName}</td>
                <td class="${statusClass}">${ticket.status}</td>
                <td>${actionButtons}</td>
            </tr>`;
            
            tableBody.innerHTML += row; 
        });
    } catch (error) {
        console.error('Error fetching tickets:', error);
        tableBody.innerHTML = `<tr><td colspan="5" style="color: red; text-align: center;">เกิดข้อผิดพลาดในการโหลดข้อมูล: ${error.message} (แน่ใจนะว่ารัน Backend ทิ้งไว้?)</td></tr>`;
    }
}

// 2. ฟังก์ชันสร้างตั๋วใบใหม่ (CREATE)
async function submitTicket() {
    const techName = document.getElementById('techName').value.trim();
    const partName = document.getElementById('partName').value.trim();

    if (!techName || !partName) {
        alert("⚠️ กรุณากรอกชื่อช่างและชื่ออะไหล่ให้ครบถ้วน!");
        return;
    }

    const newTicket = { 
        technicianName: techName, 
        partName: partName 
    };

    try {
        const response = await fetch(apiUrl, {
            method: 'POST',
            headers: { 
                'Content-Type': 'application/json' 
            },
            body: JSON.stringify(newTicket)
        });

        if (response.ok) {
            alert("✅ สร้างใบเบิกสำเร็จ!");
            // ล้างค่าในกล่องข้อความ
            document.getElementById('techName').value = '';
            document.getElementById('partName').value = '';
            // โหลดข้อมูลใหม่มาแสดง
            loadTickets();
        } else {
            alert("❌ ระบบหลังบ้านแจ้ง Error รหัส: " + response.status);
        }
    } catch (error) {
        console.error('Error submitting ticket:', error);
        alert("❌ เชื่อมต่อหลังบ้านไม่ได้: " + error.message);
    }
}

// 3. ฟังก์ชันเปลี่ยนสถานะเป็นอนุมัติ (UPDATE)
async function approveTicket(id) {
    if (confirm('ยืนยันการอนุมัติตั๋วใบนี้?')) {
        try {
            const response = await fetch(`${apiUrl}/${id}/approve`, { 
                method: 'PUT' 
            });
            
            if (response.ok) {
                alert("✅ อนุมัติสำเร็จ!");
                loadTickets(); // โหลดตารางใหม่
            } else {
                alert("❌ ระบบหลังบ้านแจ้ง Error รหัส: " + response.status);
            }
        } catch (error) {
            console.error('Error approving ticket:', error);
            alert("❌ เชื่อมต่อหลังบ้านไม่ได้: " + error.message);
        }
    }
}

// 4. ฟังก์ชันลบข้อมูล (DELETE)
async function deleteTicket(id) {
    if (confirm('คุณแน่ใจหรือไม่ที่จะลบตั๋วใบนี้?')) {
        try {
            const response = await fetch(`${apiUrl}/${id}`, { 
                method: 'DELETE' 
            });
            
            if (response.ok) {
                alert("🗑️ ลบข้อมูลสำเร็จ!");
                loadTickets(); // โหลดตารางใหม่
            } else {
                alert("❌ ระบบหลังบ้านแจ้ง Error รหัส: " + response.status);
            }
        } catch (error) {
            console.error('Error deleting ticket:', error);
            alert("❌ เชื่อมต่อหลังบ้านไม่ได้: " + error.message);
        }
    }
}

// สั่งให้โหลดข้อมูลทันทีที่เปิดหน้าเว็บ
loadTickets();