import http from "k6/http";
import { check, sleep } from "k6";

export const options = {
  stages: [
    { duration: "10s", target: 15 },
    { duration: "20s", target: 30 },
    { duration: "5s", target: 0 },
  ],
  thresholds: {
    http_req_duration: ["p(95)<300"],
    http_req_failed: ["rate<0.01"],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:8080";

export function setup() {
  const suffix = Date.now().toString().slice(-6);
  const subdomain = __ENV.TENANT || `k6scan${suffix}`;
  const email = __ENV.ADMIN_EMAIL || `admin@${subdomain}.eduflow.lk`;
  const password = __ENV.ADMIN_PASSWORD || "ScanLoad@123!";

  if (!__ENV.TENANT) {
    const signupRes = http.post(
      `${BASE_URL}/api/public/signup`,
      JSON.stringify({
        instituteName: `k6 Scan Load ${suffix}`,
        subdomain,
        adminFullName: "Load Runner",
        adminEmail: email,
        adminPassword: password,
        planCode: "Growth",
        phone: "+94771234567",
      }),
      { headers: { "Content-Type": "application/json" } },
    );
    check(signupRes, { "signup succeeded": (r) => r.status === 200 });
  }

  const loginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email, password }),
    {
      headers: {
        "Content-Type": "application/json",
        "X-Tenant": subdomain,
      },
    },
  );
  check(loginRes, { "login succeeded": (r) => r.status === 200 });
  const token = loginRes.json("accessToken");
  const authHeaders = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${token}`,
    "X-Tenant": subdomain,
  };

  const levelsRes = http.get(`${BASE_URL}/api/education-levels`, { headers: authHeaders });
  const levels = levelsRes.json();
  const levelId = levels.find((l) => l.isEnabled)?.id || levels[0].id;

  const batchRes = http.post(
    `${BASE_URL}/api/batches`,
    JSON.stringify({
      name: "k6 A/L Physics",
      code: "K6PHY",
      educationLevelId: levelId,
      academicYear: "2026",
    }),
    { headers: authHeaders },
  );
  const batchId = batchRes.json("id");

  const subjectRes = http.post(
    `${BASE_URL}/api/subjects`,
    JSON.stringify({
      name: "Physics",
      code: "PHY",
      educationLevelId: levelId,
    }),
    { headers: authHeaders },
  );
  const subjectId = subjectRes.json("id");

  const teachersRes = http.get(`${BASE_URL}/api/class-schedules/teachers`, { headers: authHeaders });
  const teachers = teachersRes.json();
  const teacherUserId = teachers[0]?.id;

  const scheduleRes = http.post(
    `${BASE_URL}/api/class-schedules`,
    JSON.stringify({
      name: "k6 Morning Session",
      batchId,
      subjectId,
      teacherUserId,
      weekday: 1,
      startMinutes: 480,
      endMinutes: 600,
      room: "Hall 1",
      isActive: true,
    }),
    { headers: authHeaders },
  );
  const scheduleId = scheduleRes.json("id");

  const studentRes = http.post(
    `${BASE_URL}/api/students`,
    JSON.stringify({
      fullName: "Kasun Perera",
      guardianName: "Nimal Perera",
      guardianPhone: "+94771112233",
      address: "Colombo 07",
      educationLevelId: levelId,
      gender: "Male",
      status: "Active",
      batchIds: [batchId],
    }),
    { headers: authHeaders },
  );
  const studentCode = studentRes.json("studentCode");

  const openRes = http.post(
    `${BASE_URL}/api/attendance/sessions`,
    JSON.stringify({ classScheduleId: scheduleId }),
    { headers: authHeaders },
  );
  const sessionId = openRes.json("id");

  http.post(`${BASE_URL}/api/attendance/sessions/${sessionId}/start`, "{}", {
    headers: authHeaders,
  });

  return { token, subdomain, sessionId, studentCode };
}

export default function (data) {
  const res = http.post(
    `${BASE_URL}/api/attendance/scan`,
    JSON.stringify({
      code: data.studentCode,
      sessionId: data.sessionId,
    }),
    {
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${data.token}`,
        "X-Tenant": data.subdomain,
      },
    },
  );

  check(res, {
    "scan status 200": (r) => r.status === 200,
    "scan outcome valid": (r) => {
      const outcome = r.json("outcome");
      return outcome === "Marked" || outcome === "AlreadyMarked";
    },
  });

  sleep(0.05);
}
